using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Service.Business;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

/// <summary>Where uploaded images are kept. The file name is always generated here, never taken from the upload.</summary>
public interface IImageStore
{
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct);
}

public sealed class FileSystemImageStore : IImageStore
{
    private readonly string _folder;
    private readonly string _publicPrefix;

    public FileSystemImageStore(string folder, string publicPrefix)
    {
        _folder = folder;
        _publicPrefix = publicPrefix.TrimEnd('/');
        Directory.CreateDirectory(_folder);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct)
    {
        var name = Guid.NewGuid().ToString("N") + extension;
        await File.WriteAllBytesAsync(Path.Combine(_folder, name), content, ct);
        return $"{_publicPrefix}/{name}";
    }
}

public sealed class PhotoService : ServiceBase
{
    private readonly IImageStore _store;

    public PhotoService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options, IImageStore store)
        : base(db, user, clock, options) => _store = store;

    /// <summary>
    /// Replaces the person's photo. The type is decided from the file's own bytes, the size is capped, alt text is
    /// required, and the previous file is left on disk (history is not deleted).
    /// </summary>
    public async Task<ServiceResult> SetPhotoAsync(int personId, byte[] content, string? altText, CancellationToken ct = default)
    {
        var person = await Db.People.FirstOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null) return Missing("That person");
        if (!CanEditRecord(person.Stage)) return Denied("You do not have permission to change this record.");

        var report = UploadRules.ValidateImage(content.Length, content.AsSpan(0, Math.Min(content.Length, 16)));
        if (string.IsNullOrWhiteSpace(altText)) report.Error("upload.alt_required", "Describe the photo for people who cannot see it.", "PhotoAltText");
        if (!report.IsValid) return Result.Invalid(report);

        var detected = UploadRules.DetectImage(content)!;
        return await InTransactionAsync(async () =>
        {
            var url = await _store.SaveAsync(content, detected.Extension, ct);
            person.PhotoUrl = url;
            person.PhotoAltText = altText!.Trim();
            person.PhotoContentType = detected.ContentType;
            person.PhotoSizeBytes = content.Length;
            Audit("person.photo_changed", nameof(Person), person.Id, $"Photo set for {person.FullName}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }
}
