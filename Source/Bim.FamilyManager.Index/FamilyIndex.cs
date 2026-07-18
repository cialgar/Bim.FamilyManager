using System.IO;
using Microsoft.Data.Sqlite;

namespace Bim.FamilyManager.Index;

/// <summary>
///     Owns the SQLite database that stores the persistent family index.
/// </summary>
/// <remarks>
///     The index replaces the per-session directory scan with a persistent catalog of family files and
///     their metadata, refreshed incrementally by <see cref="IndexScanner" /> and queried by
///     <see cref="IndexQuery" />. The database uses WAL journaling so that the UI can read while a scan
///     is writing, and an FTS5 table for instant full-text search over names, folders, categories and
///     family type names.
///     The <c>category</c> column stores the localized category term exactly as authored in the family
///     file. The <c>category_key</c> column is reserved for a language-independent category key filled
///     by the normalization map planned for phase 3 (the PartAtom stream carries no stable category
///     identifier; see ARCHITECTURE.md). The OmniClass number is captured separately when present.
/// </remarks>
public sealed class FamilyIndex
{
    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS sources (
            id            INTEGER PRIMARY KEY,
            root_path     TEXT NOT NULL UNIQUE,
            last_scan_utc TEXT
        );

        CREATE TABLE IF NOT EXISTS families (
            id              INTEGER PRIMARY KEY,
            source_id       INTEGER NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
            path            TEXT NOT NULL UNIQUE,
            name            TEXT NOT NULL,
            folder          TEXT NOT NULL,
            size            INTEGER NOT NULL,
            mtime_utc_ticks INTEGER NOT NULL,
            product_version TEXT,
            category        TEXT,
            category_key    TEXT,
            omniclass       TEXT,
            updated_utc     TEXT,
            indexed_at_utc  TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_families_source_folder ON families(source_id, folder);
        CREATE INDEX IF NOT EXISTS idx_families_category ON families(category);

        CREATE TABLE IF NOT EXISTS symbols (
            id        INTEGER PRIMARY KEY,
            family_id INTEGER NOT NULL REFERENCES families(id) ON DELETE CASCADE,
            name      TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_symbols_family ON symbols(family_id);

        CREATE TABLE IF NOT EXISTS tags (
            id   INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS family_tags (
            family_id INTEGER NOT NULL REFERENCES families(id) ON DELETE CASCADE,
            tag_id    INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
            PRIMARY KEY (family_id, tag_id)
        );

        CREATE TABLE IF NOT EXISTS favorites (
            family_id INTEGER PRIMARY KEY REFERENCES families(id) ON DELETE CASCADE
        );

        CREATE VIRTUAL TABLE IF NOT EXISTS families_fts USING fts5(name, folder, category, symbols);
        """;

    private readonly string _connectionString;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FamilyIndex" /> class.
    /// </summary>
    /// <param name="databasePath">The full path of the SQLite database file. Created on first use.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="databasePath" /> is null or whitespace.</exception>
    public FamilyIndex(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(@"Database path cannot be null or empty.", nameof(databasePath));
        }

        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    /// <summary>
    ///     Gets the full path of the SQLite database file.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    ///     Creates an index instance using the default location <c>%AppData%\FamilyManager\index.db</c>.
    /// </summary>
    /// <returns>A <see cref="FamilyIndex" /> rooted at the default database path.</returns>
    public static FamilyIndex CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new FamilyIndex(Path.Combine(appData, "FamilyManager", "index.db"));
    }

    /// <summary>
    ///     Ensures that the database file and its schema exist.
    /// </summary>
    /// <remarks>
    ///     The schema is created with <c>IF NOT EXISTS</c> statements, so calling this method repeatedly
    ///     is cheap and safe. Callers should invoke it once before scanning or querying.
    /// </remarks>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    ///     Opens a configured connection to the index database.
    /// </summary>
    /// <returns>An open <see cref="SqliteConnection" /> with WAL journaling and foreign keys enabled.</returns>
    /// <remarks>The caller owns the connection and must dispose it.</remarks>
    internal SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }
}
