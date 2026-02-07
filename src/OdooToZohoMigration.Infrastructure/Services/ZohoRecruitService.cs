using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OdooToZohoMigration.Core.DTOs.Migration;
using OdooToZohoMigration.Core.DTOs.Zoho;
using OdooToZohoMigration.Core.Entities;
using OdooToZohoMigration.Core.Interfaces;
using OdooToZohoMigration.Infrastructure.Configuration;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace OdooToZohoMigration.Infrastructure.Services;

public class ZohoRecruitService : IZohoRecruitService
{
    private readonly HttpClient _httpClient;
    private readonly ZohoRecruitSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ZohoRecruitService> _logger;
    private const string TokenCacheKey = "ZohoAccessToken";

    public ZohoRecruitService(
        HttpClient httpClient,
        IOptions<ZohoRecruitSettings> settings,
        IMemoryCache cache,
        ILogger<ZohoRecruitService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    #region Authentication

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
            return cachedToken;

        return await RefreshAccessTokenAsync(ct);
    }

    public async Task<string> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        var tokenUrl = $"https://{_settings.AccountsDomain}/oauth/v2/token";

        var parameters = new Dictionary<string, string>
        {
            ["refresh_token"] = _settings.RefreshToken,
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret,
            ["grant_type"] = "refresh_token"
        };

        var content = new FormUrlEncodedContent(parameters);
        _logger.LogInformation("Refreshing Zoho access token...");

        var response = await _httpClient.PostAsync(tokenUrl, content, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to refresh token. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            throw new InvalidOperationException($"Failed to refresh Zoho access token: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<ZohoTokenResponse>(responseContent);
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException($"Invalid token response: {responseContent}");

        _cache.Set(TokenCacheKey, tokenResponse.AccessToken, TimeSpan.FromMinutes(_settings.TokenCacheMinutes));
        _logger.LogInformation("Successfully refreshed Zoho access token.");
        return tokenResponse.AccessToken;
    }

    #endregion

    #region Job Openings

    public async Task<EntityMigrationResult> CreateJobOpeningAsync(Job job, CancellationToken ct = default)
    {
        try
        {
            var request = new ZohoJobOpeningRequest
            {
                Data = new List<ZohoJobOpeningData> { MapJobToZohoData(job) }
            };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Post, "/recruit/v2/Job_Openings", request, ct);

            var record = response?.Data?.FirstOrDefault();
            if (record?.Status == "success")
                return new EntityMigrationResult { Success = true, ZohoId = record.Details?.Id };

            return new EntityMigrationResult { Success = false, ErrorMessage = record?.Message ?? "Unknown error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating job opening for Job ID: {JobId}", job.Id);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    public async Task<EntityMigrationResult> UpdateJobOpeningAsync(string zohoId, Job job, CancellationToken ct = default)
    {
        try
        {
            var request = new ZohoJobOpeningRequest
            {
                Data = new List<ZohoJobOpeningData> { MapJobToZohoData(job) }
            };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Put, $"/recruit/v2/Job_Openings/{zohoId}", request, ct);

            var record = response?.Data?.FirstOrDefault();
            return new EntityMigrationResult
            {
                Success = record?.Status == "success",
                ZohoId = zohoId,
                ErrorMessage = record?.Status != "success" ? record?.Message : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job opening {ZohoId}", zohoId);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    /// <summary>
    /// Batch create job openings. Zoho supports up to 100 per API call.
    /// Results are returned IN THE SAME ORDER as input - use index-based mapping.
    /// </summary>
    public async Task<List<EntityMigrationResult>> CreateJobOpeningsBatchAsync(
        IEnumerable<Job> jobs, CancellationToken ct = default)
    {
        var results = new List<EntityMigrationResult>();
        var jobList = jobs.ToList();
        var batches = jobList.Chunk(_settings.MaxBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                var request = new ZohoJobOpeningRequest
                {
                    Data = batch.Select(MapJobToZohoData).ToList()
                };

                var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                    HttpMethod.Post, "/recruit/v2/Job_Openings", request, ct);

                if (response?.Data != null)
                {
                    // IMPORTANT: Zoho returns results in the SAME ORDER as the input.
                    // Map by index - no need for additional GET calls.
                    foreach (var record in response.Data)
                    {
                        results.Add(new EntityMigrationResult
                        {
                            Success = record.Status == "success",
                            ZohoId = record.Details?.Id,
                            ErrorMessage = record.Status != "success" ? record.Message : null
                        });
                    }
                }
                else
                {
                    // If no response data, mark all as failed
                    foreach (var _ in batch)
                        results.Add(new EntityMigrationResult { Success = false, ErrorMessage = "No response data from Zoho" });
                }

                await Task.Delay(_settings.ApiDelayMs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch job creation ({Count} jobs)", batch.Length);
                foreach (var _ in batch)
                    results.Add(new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() });
            }
        }

        return results;
    }

    private static ZohoJobOpeningData MapJobToZohoData(Job job) => new()
    {
        Job_Opening_Name = job.Title,
        PostingTitle = job.Title,
        JobDescription = job.Description,
        DepartmentName = string.IsNullOrEmpty(job.Department) ? "Aventra Group" : job.Department,
        JobOpeningStatus = MapJobStatus(job.Status)
    };

    private static string? MapJobStatus(string? status) => status?.ToLower() switch
    {
        "open" or "active" or "published" => "In-progress",
        "closed" or "filled" => "Closed",
        "on-hold" or "onhold" or "hold" => "On-hold",
        "draft" => "Waiting-for-approval",
        _ => status ?? "In-progress"
    };

    #endregion

    #region Candidates

    public async Task<EntityMigrationResult> CreateCandidateAsync(Candidate candidate, CancellationToken ct = default)
    {
        try
        {
            var data = MapCandidateToZohoData(candidate);
            if (data == null)
                return new EntityMigrationResult { Success = false, ErrorMessage = "Failed to map candidate data" };

            var request = new ZohoCandidateRequest { Data = new List<ZohoCandidateData> { data } };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Post, "/recruit/v2/Candidates", request, ct);

            var record = response?.Data?.FirstOrDefault();
            if (record?.Status == "success")
                return new EntityMigrationResult { Success = true, ZohoId = record.Details?.Id };

            return new EntityMigrationResult { Success = false, ErrorMessage = record?.Message ?? "Unknown error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating candidate ID: {CandidateId}", candidate.Id);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    public async Task<EntityMigrationResult> UpdateCandidateAsync(string zohoId, Candidate candidate, CancellationToken ct = default)
    {
        try
        {
            var data = MapCandidateToZohoData(candidate);
            if (data == null)
                return new EntityMigrationResult { Success = false, ErrorMessage = "Failed to map candidate data" };

            var request = new ZohoCandidateRequest { Data = new List<ZohoCandidateData> { data } };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Put, $"/recruit/v2/Candidates/{zohoId}", request, ct);

            var record = response?.Data?.FirstOrDefault();
            return new EntityMigrationResult
            {
                Success = record?.Status == "success",
                ZohoId = zohoId,
                ErrorMessage = record?.Status != "success" ? record?.Message : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating candidate {ZohoId}", zohoId);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    public async Task<ZohoCandidateMinimal?> GetCandidateByIdAsync(string? zohoId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(zohoId)) return null;

        var response = await SendZohoRequestAsync<ZohoCandidateSearchResponse>(
            HttpMethod.Get, $"/recruit/v2/Candidates/{zohoId}", null, ct);

        return response?.Data?.FirstOrDefault();
    }

    /// <summary>
    /// OPTIMIZED: Batch create candidates using Zoho bulk API.
    /// 
    /// KEY CHANGE: Zoho returns results in the SAME ORDER as the input records.
    /// We use index-based mapping to match results back to candidates.
    /// This eliminates the need for N individual GET calls after batch creation
    /// (which was causing massive API overhead - e.g., 30,000 extra calls for 30k candidates).
    /// 
    /// Each result includes the candidate's email from the INPUT (not from a GET call),
    /// allowing the caller to match results without additional API round-trips.
    /// </summary>
    public async Task<List<EntityMigrationResult>> CreateCandidatesBatchAsync(
        IEnumerable<Candidate> candidates, CancellationToken ct = default)
    {
        var results = new List<EntityMigrationResult>();
        var candidateList = candidates.ToList();
        var batches = candidateList.Chunk(_settings.MaxBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                var mappedData = batch
                    .Select(MapCandidateToZohoData)
                    .Where(d => d != null)
                    .Cast<ZohoCandidateData>()
                    .ToList();

                var request = new ZohoCandidateRequest { Data = mappedData };

                var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                    HttpMethod.Post, "/recruit/v2/Candidates", request, ct);

                if (response?.Data != null)
                {
                    // INDEX-BASED MAPPING: response[i] corresponds to batch[i]
                    // No need for individual GET calls to fetch email back.
                    for (int i = 0; i < response.Data.Count && i < batch.Length; i++)
                    {
                        var record = response.Data[i];
                        var sourceCandidate = batch[i];

                        results.Add(new EntityMigrationResult
                        {
                            Success = record.Status == "success",
                            ZohoId = record.Details?.Id,
                            // Use the email from the INPUT candidate, not from a GET call
                            ZohoCandidateEmail = sourceCandidate.Email?.ToLowerInvariant(),
                            ErrorMessage = record.Status != "success" ? record.Message : null
                        });
                    }

                    // Handle case where response has fewer items than batch
                    for (int i = response.Data.Count; i < batch.Length; i++)
                    {
                        results.Add(new EntityMigrationResult
                        {
                            Success = false,
                            ZohoCandidateEmail = batch[i].Email?.ToLowerInvariant(),
                            ErrorMessage = "No response from Zoho for this record"
                        });
                    }
                }
                else
                {
                    foreach (var candidate in batch)
                    {
                        results.Add(new EntityMigrationResult
                        {
                            Success = false,
                            ZohoCandidateEmail = candidate.Email?.ToLowerInvariant(),
                            ErrorMessage = "No response data from Zoho"
                        });
                    }
                }

                await Task.Delay(_settings.ApiDelayMs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch candidate creation ({Count} candidates)", batch.Length);
                foreach (var candidate in batch)
                {
                    results.Add(new EntityMigrationResult
                    {
                        Success = false,
                        ZohoCandidateEmail = candidate.Email?.ToLowerInvariant(),
                        ErrorMessage = ex.Message,
                        ErrorDetails = ex.ToString()
                    });
                }
            }
        }

        return results;
    }

    private static ZohoCandidateData? MapCandidateToZohoData(Candidate candidate)
    {
        if (candidate == null) return null;

        var (firstName, lastName) = SplitName(candidate.Name);

        return new ZohoCandidateData
        {
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            Email = SafeSanitizeEmail(candidate.Email),
            Phone = SanitizePhone(candidate.Phone),
            Mobile = SanitizePhone(candidate.Mobile),
            City = candidate.CurrentLocation?.Trim(),
            DateOfBirth = candidate.DateOfBirth?.ToString("yyyy-MM-dd"),
            Gender = MapGender(candidate.Gender),
            ExperienceInYears = candidate.TotalExperienceYears.GetValueOrDefault().ToString(),
            SkillSet = string.IsNullOrWhiteSpace(candidate.Skills) ? null : candidate.Skills.Trim(),
            LinkedIn = string.IsNullOrWhiteSpace(candidate.LinkedInUrl) ? null : candidate.LinkedInUrl.Trim(),
            CandidateSource = "Odoo Migration"
        };
    }

    private static string? SafeSanitizeEmail(string? email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            email = email.Split('|', ',', ';')[0];
            email = email.Replace(" ", string.Empty).Trim().TrimEnd('.', '|');
            return MailAddress.TryCreate(email, out _) ? email : null;
        }
        catch { return null; }
    }

    private static string? SanitizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digitsOnly) ? null : digitsOnly;
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return ("Unknown", "Candidate");
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("Unknown", "Candidate");
        if (parts.Length == 1) return (parts[0], "-");
        return (parts[0], parts[1]);
    }

    private static string? MapGender(string? gender)
    {
        if (string.IsNullOrEmpty(gender)) return null;
        return gender.ToLower() switch
        {
            "m" or "male" => "Male",
            "f" or "female" => "Female",
            _ => gender
        };
    }

    #endregion

    #region Applications

    public async Task<EntityMigrationResult> AssociateCandidateWithJobAsync(
        string candidateZohoId, string jobZohoId, string? comments = null, CancellationToken ct = default)
    {
        try
        {
            var request = new
            {
                data = new[]
                {
                    new
                    {
                        ids = new List<string> { candidateZohoId },
                        jobids = new List<string> { jobZohoId },
                        comments = comments ?? "Migrated from Odoo"
                    }
                }
            };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Put, "/recruit/v2/Candidates/actions/associate", request, ct);

            var record = response?.Data?.FirstOrDefault();
            return new EntityMigrationResult
            {
                Success = record?.Status == "success" || record?.Code == "SUCCESS",
                ZohoId = record?.Details?.Id,
                ErrorMessage = record?.Status != "success" ? record?.Message : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error associating candidate {CandidateId} with job {JobId}",
                candidateZohoId, jobZohoId);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    public async Task<EntityMigrationResult> UpdateApplicationStatusAsync(
        string applicationZohoId, string status, CancellationToken ct = default)
    {
        try
        {
            var request = new ZohoApplicationStatusRequest
            {
                Data = new List<ZohoApplicationStatusData>
                {
                    new() { Ids = new List<string> { applicationZohoId }, CandidateStatus = MapApplicationStatus(status) }
                }
            };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Put, "/recruit/v2/Applications/status", request, ct);

            var record = response?.Data?.FirstOrDefault();
            return new EntityMigrationResult
            {
                Success = record?.Status == "success",
                ZohoId = applicationZohoId,
                ErrorMessage = record?.Status != "success" ? record?.Message : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating application status for {ApplicationId}", applicationZohoId);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    private static string MapApplicationStatus(string? status) => status?.ToLower() switch
    {
        "new" or "initial qualification" => "New",
        "screening" or "first interview" => "Screening",
        "interview" or "second interview" => "Interview",
        "offer" or "contract proposal" => "Offer",
        "hired" or "contract signed" => "Hired",
        "rejected" or "refused" => "Rejected",
        _ => status ?? "New"
    };

    #endregion

    #region CVs

    public async Task<CvUploadResult> UploadCandidateCvAsync(
        string candidateZohoId, Stream cvStream, string fileName, string contentType,
        CancellationToken ct = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(ct);
            var url = $"https://{_settings.ApiDomain}/recruit/v2/Candidates/{candidateZohoId}/Attachments?attachments_category=Resume";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token);

            using var formContent = new MultipartFormDataContent();
            using var streamContent = new StreamContent(cvStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            formContent.Add(streamContent, "file", fileName);
            request.Content = formContent;

            var response = await _httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("CV upload failed. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode, responseContent);
                return new CvUploadResult { Success = false, ErrorMessage = $"HTTP {response.StatusCode}: {responseContent}" };
            }

            var uploadResponse = JsonSerializer.Deserialize<ZohoApiResponse<ZohoRecordResponse>>(responseContent);
            var record = uploadResponse?.Data?.FirstOrDefault();

            return new CvUploadResult
            {
                Success = record?.Status == "success",
                AttachmentId = record?.Details?.Id,
                ErrorMessage = record?.Status != "success" ? record?.Message : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading CV for candidate {CandidateId}", candidateZohoId);
            return new CvUploadResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    #endregion

    #region Notes (Comments, Summaries, History)

    public async Task<EntityMigrationResult> CreateNoteAsync(
        string parentModule, string parentId, string noteTitle, string noteContent,
        CancellationToken ct = default)
    {
        try
        {
            var request = new ZohoNoteRequest
            {
                Data = new List<ZohoNoteData>
                {
                    new() { ParentId = parentId, SeModule = parentModule, NoteTitle = noteTitle, NoteContent = noteContent }
                }
            };

            var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                HttpMethod.Post, "/recruit/v2/Notes", request, ct);

            var record = response?.Data?.FirstOrDefault();
            return new EntityMigrationResult
            {
                Success = record?.Status == "success",
                ZohoId = record?.Details?.Id,
                ErrorMessage = record?.Status != "success" ? record?.Message : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating note for {Module} {ParentId}", parentModule, parentId);
            return new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
        }
    }

    /// <summary>
    /// OPTIMIZED: Batch create notes. Zoho Notes API supports up to 100 per batch.
    /// Results are returned in the same order as input.
    /// </summary>
    public async Task<List<EntityMigrationResult>> CreateNotesBatchAsync(
        string parentModule,
        List<(string ParentId, string Title, string Content)> notes,
        CancellationToken ct = default)
    {
        var results = new List<EntityMigrationResult>();
        var batches = notes.Chunk(_settings.MaxBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                var request = new ZohoNoteRequest
                {
                    Data = batch.Select(n => new ZohoNoteData
                    {
                        ParentId = n.ParentId,
                        SeModule = parentModule,
                        NoteTitle = n.Title,
                        NoteContent = n.Content
                    }).ToList()
                };

                var response = await SendZohoRequestAsync<ZohoApiResponse<ZohoRecordResponse>>(
                    HttpMethod.Post, "/recruit/v2/Notes", request, ct);

                if (response?.Data != null)
                {
                    foreach (var record in response.Data)
                    {
                        results.Add(new EntityMigrationResult
                        {
                            Success = record.Status == "success",
                            ZohoId = record.Details?.Id,
                            ErrorMessage = record.Status != "success" ? record.Message : null
                        });
                    }
                }
                else
                {
                    foreach (var _ in batch)
                        results.Add(new EntityMigrationResult { Success = false, ErrorMessage = "No response data from Zoho" });
                }

                await Task.Delay(_settings.ApiDelayMs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch note creation ({Count} notes)", batch.Length);
                foreach (var _ in batch)
                    results.Add(new EntityMigrationResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString() });
            }
        }

        return results;
    }

    #endregion

    #region Helper Methods

    private async Task<T?> SendZohoRequestAsync<T>(
        HttpMethod method, string endpoint, object? body = null,
        CancellationToken ct = default) where T : class
    {
        var token = await GetAccessTokenAsync(ct);
        var url = $"https://{_settings.ApiDomain}{endpoint}";

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            _logger.LogDebug("Zoho API Request: {Method} {Url}", method, url);
        }

        var response = await _httpClient.SendAsync(request, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);

        _logger.LogDebug("Zoho API Response: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Zoho API error. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _cache.Remove(TokenCacheKey);
                throw new InvalidOperationException("Zoho token expired. Please retry the operation.");
            }

            throw new HttpRequestException($"Zoho API returned {response.StatusCode}: {responseContent}");
        }

        return JsonSerializer.Deserialize<T>(responseContent);
    }

    #endregion
}
