using System.Text.Json.Serialization;

namespace OdooToZohoMigration.Core.DTOs.Zoho;

// ===== Token =====
public class ZohoTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

// ===== Generic API Response =====
public class ZohoApiResponse<T>
{
    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }
}

public class ZohoRecordResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("details")]
    public ZohoRecordDetails? Details { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class ZohoRecordDetails
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

// ===== Job Openings =====
public class ZohoJobOpeningRequest
{
    [JsonPropertyName("data")]
    public List<ZohoJobOpeningData> Data { get; set; } = new();
}

public class ZohoJobOpeningData
{
    [JsonPropertyName("Job_Opening_Name")]
    public string? Job_Opening_Name { get; set; }

    [JsonPropertyName("Posting_Title")]
    public string? PostingTitle { get; set; }

    [JsonPropertyName("Job_Description")]
    public string? JobDescription { get; set; }

    [JsonPropertyName("Department_Name")]
    public string? DepartmentName { get; set; }

    [JsonPropertyName("Job_Opening_Status")]
    public string? JobOpeningStatus { get; set; }
}

// ===== Candidates =====
public class ZohoCandidateRequest
{
    [JsonPropertyName("data")]
    public List<ZohoCandidateData> Data { get; set; } = new();
}

public class ZohoCandidateData
{
    [JsonPropertyName("First_Name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("Last_Name")]
    public string? LastName { get; set; }

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("Phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("Mobile")]
    public string? Mobile { get; set; }

    [JsonPropertyName("City")]
    public string? City { get; set; }

    [JsonPropertyName("Date_of_Birth")]
    public string? DateOfBirth { get; set; }

    [JsonPropertyName("Gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("Experience_in_Years")]
    public string? ExperienceInYears { get; set; }

    [JsonPropertyName("Skill_Set")]
    public string? SkillSet { get; set; }

    [JsonPropertyName("LinkedIn")]
    public string? LinkedIn { get; set; }

    [JsonPropertyName("Candidate_Source")]
    public string? CandidateSource { get; set; }
}

// ===== Candidate Search =====
public class ZohoCandidateSearchResponse
{
    [JsonPropertyName("data")]
    public List<ZohoCandidateMinimal>? Data { get; set; }
}

public class ZohoCandidateMinimal
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("First_Name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("Last_Name")]
    public string? LastName { get; set; }
}

// ===== Applications =====
public class ZohoApplicationStatusRequest
{
    [JsonPropertyName("data")]
    public List<ZohoApplicationStatusData> Data { get; set; } = new();
}

public class ZohoApplicationStatusData
{
    [JsonPropertyName("ids")]
    public List<string> Ids { get; set; } = new();

    [JsonPropertyName("Candidate_Status")]
    public string? CandidateStatus { get; set; }
}

// ===== Notes =====
public class ZohoNoteRequest
{
    [JsonPropertyName("data")]
    public List<ZohoNoteData> Data { get; set; } = new();
}

public class ZohoNoteData
{
    [JsonPropertyName("Parent_Id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("se_module")]
    public string? SeModule { get; set; }

    [JsonPropertyName("Note_Title")]
    public string? NoteTitle { get; set; }

    [JsonPropertyName("Note_Content")]
    public string? NoteContent { get; set; }
}
