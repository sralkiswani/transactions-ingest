using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using transactions_ingest.Core.Import.Mock;

namespace transactions_ingest.Core.Import;

/// <summary>
/// Validates <see cref="ImportOptions"/> including its nested sections, which data annotations alone do not
/// recurse into. Runs on start (ValidateOnStart), so bad config stops the app instead of a run.
/// </summary>
public sealed class ImportOptionsValidator : IValidateOptions<ImportOptions>
{
    public ValidateOptionsResult Validate(string? name, ImportOptions options)
    {
        var errors = new List<string>();
        Annotations(options, "Import", errors);
        Annotations(options.RevocationGuard, "Import:RevocationGuard", errors);
        Annotations(options.MockSources, "Import:MockSources", errors);

        // Names differing only by case are distinct sources but would share a file on Windows.
        var clashes = options.MockSources.Names
            .GroupBy(MockSourceFiles.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Import:MockSources:Names {string.Join(", ", g.Select(n => $"'{n}'"))} map to the same file '{g.Key}'.");
        errors.AddRange(clashes);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Annotations(object instance, string section, List<string> errors)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        errors.AddRange(results.Select(r => $"{section}: {r.ErrorMessage}"));
    }
}
