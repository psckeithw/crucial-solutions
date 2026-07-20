using ServiceNowToAdo.Models;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Tests;

public class PayloadValidatorTests
{
    private readonly IPayloadValidator _validator = new PayloadValidator();

    [Fact]
    public void TryValidate_NullPayload_ReturnsFalse()
    {
        var valid = _validator.TryValidate(null, out var error);
        Assert.False(valid);
        Assert.Equal("Payload body is missing or not valid JSON.", error);
    }

    [Theory]
    [InlineData("IncidentNumber")]
    [InlineData("TeamProject")]
    [InlineData("Title")]
    [InlineData("Description")]
    public void TryValidate_MissingEachRequiredField_ReturnsFalse(string fieldToBlank)
    {
        var payload = new ServiceNowPayload
        {
            IncidentNumber = "INC1",
            TeamProject = "UPO",
            Title = "T",
            Description = "D"
        };
        typeof(ServiceNowPayload).GetProperty(fieldToBlank)!.SetValue(payload, "");

        var valid = _validator.TryValidate(payload, out var error);

        Assert.False(valid);
        Assert.Contains(fieldToBlank, error);
    }

    [Fact]
    public void TryValidate_WhitespaceField_ReturnsFalse()
    {
        var payload = new ServiceNowPayload
        {
            IncidentNumber = "   ",
            TeamProject = "UPO",
            Title = "T",
            Description = "D"
        };
        var valid = _validator.TryValidate(payload, out var error);
        Assert.False(valid);
        Assert.Contains("IncidentNumber", error);
    }

    [Fact]
    public void TryValidate_ValidPayload_ReturnsTrue()
    {
        var payload = new ServiceNowPayload
        {
            IncidentNumber = "INC123",
            TeamProject = "UPO",
            Title = "Title",
            Description = "Desc"
        };
        var valid = _validator.TryValidate(payload, out var error);
        Assert.True(valid);
        Assert.Equal("", error);
    }

    [Fact]
    public void TryValidate_ValidPayloadWithIncidentUrl_ReturnsTrue()
    {
        var payload = new ServiceNowPayload
        {
            IncidentNumber = "INC123",
            TeamProject = "UPO",
            Title = "Title",
            Description = "Desc",
            IncidentUrl = "https://example.com/inc/123"
        };
        var valid = _validator.TryValidate(payload, out var error);
        Assert.True(valid);
        Assert.Equal("", error);
    }
}
