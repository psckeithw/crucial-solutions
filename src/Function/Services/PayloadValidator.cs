using ServiceNowToAdo.Models;

namespace ServiceNowToAdo.Services;

public interface IPayloadValidator
{
    bool TryValidate(ServiceNowPayload? payload, out string error);
}

public sealed class PayloadValidator : IPayloadValidator
{
    public bool TryValidate(ServiceNowPayload? payload, out string error)
    {
        if (payload is null)
        {
            error = "Payload body is missing or not valid JSON.";
            return false;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(payload.IncidentNumber)) missing.Add("IncidentNumber");
        if (string.IsNullOrWhiteSpace(payload.TeamProject)) missing.Add("TeamProject");
        if (string.IsNullOrWhiteSpace(payload.Title)) missing.Add("Title");
        if (string.IsNullOrWhiteSpace(payload.Description)) missing.Add("Description");

        if (missing.Count > 0)
        {
            error = $"Missing or empty required field(s): {string.Join(", ", missing)}.";
            return false;
        }

        error = "";
        return true;
    }
}
