using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PdfWerk.Infrastructure.Data;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time.
/// </summary>
/// <remarks>
/// Pinned to Npgsql, and that is the whole point of the file. Without it the tooling picks up
/// whatever provider the application happens to be configured with, which for a machine with no
/// connection string set is SQLite — and a migration scaffolded that way declares every column
/// as <c>TEXT</c> or <c>INTEGER</c>. Postgres will accept that DDL and then hand back strings
/// where the model expects a Guid, so the failure surfaces far from its cause.
///
/// It happened: the first migration was written against SQLite, and the deployed database stayed
/// empty because EF refused to apply a snapshot that did not match the Npgsql model.
///
/// The connection string here is never connected to. Migrations are generated from the model, so
/// the tooling only needs to know which provider's type mappings to use.
/// </remarks>
public sealed class PdfWerkDbContextFactory : IDesignTimeDbContextFactory<PdfWerkDbContext>
{
    public PdfWerkDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PdfWerkDbContext>()
            .UseNpgsql("Host=localhost;Database=pdfwerk;Username=postgres")
            .Options;

        return new PdfWerkDbContext(options);
    }
}
