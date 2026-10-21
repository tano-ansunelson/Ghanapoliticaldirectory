using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Business;

public enum ServiceFailure { None = 0, Invalid = 1, Forbidden = 2, NotFound = 3, Conflict = 4 }

/// <summary>
/// What every service returns. Errors are business-rule messages meant to be shown to the person using the form;
/// warnings are shown but do not stop the save.
/// </summary>
public class ServiceResult
{
    public ValidationReport Report { get; set; } = new();
    public ServiceFailure Failure { get; set; }
    public bool Ok => Failure == ServiceFailure.None && Report.IsValid;
    public string? FirstError => Report.Errors.FirstOrDefault()?.Message;
}

public sealed class ServiceResult<T> : ServiceResult
{
    public T? Value { get; set; }
}

public static class Result
{
    public static ServiceResult Ok(ValidationReport? report = null) => new() { Report = report ?? new() };

    public static ServiceResult<T> Ok<T>(T value, ValidationReport? report = null) =>
        new() { Value = value, Report = report ?? new() };

    public static ServiceResult<T> Invalid<T>(ValidationReport report, T? value = default) =>
        new() { Failure = ServiceFailure.Invalid, Report = report, Value = value };

    public static ServiceResult Invalid(ValidationReport report) =>
        new() { Failure = ServiceFailure.Invalid, Report = report };

    public static ServiceResult<T> Fail<T>(ServiceFailure kind, string code, string message)
    {
        var r = new ServiceResult<T> { Failure = kind };
        r.Report.Error(code, message);
        return r;
    }

    public static ServiceResult Fail(ServiceFailure kind, string code, string message)
    {
        var r = new ServiceResult { Failure = kind };
        r.Report.Error(code, message);
        return r;
    }
}
