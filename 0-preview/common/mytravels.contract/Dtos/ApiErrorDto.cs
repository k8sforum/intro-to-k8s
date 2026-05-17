using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Dtos;

[ExcludeFromCodeCoverage]
public class ApiErrorDto
{
    public string Id { get; set; }
    public int HttpStatusCode { get; set; }
    public string Code { get; set; }
    public string Links { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public string Detail { get; set; }
}
