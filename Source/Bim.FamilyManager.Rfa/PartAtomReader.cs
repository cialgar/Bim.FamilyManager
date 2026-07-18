using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using OpenMcdf;

namespace Bim.FamilyManager.Rfa;

/// <summary>
///     Reads the metadata stored in the <c>PartAtom</c> stream of a Revit family file (*.rfa).
/// </summary>
/// <remarks>
///     Every family file is an OLE compound file whose <c>PartAtom</c> stream contains the same Atom XML
///     document that <c>Document.ExtractPartAtomFromFamilyFile</c> produces. This reader extracts, in a
///     single pass, everything the family index needs: title, product version, update date, the
///     localized family category (scheme <c>adsk:revit:grouping</c>), the OmniClass number (scheme
///     <c>std:oc1</c>, present only in model families) and the family type (symbol) names.
///     <c>Scotec.Revit.RevitFamily.RevitFamilyInfo</c> parses the same stream but exposes neither the
///     category nor the OmniClass number; this reader fills exactly that gap.
///     Only the compound file sectors belonging to the <c>PartAtom</c> stream are read; the family file
///     is never loaded into memory as a whole.
/// </remarks>
public static class PartAtomReader
{
    private const string PartAtomStreamName = "PartAtom";
    private const string CategoryScheme = "adsk:revit:grouping";
    private const string OmniClassScheme = "std:oc1";

    /// <summary>
    ///     Reads the PartAtom metadata from the specified family file.
    /// </summary>
    /// <param name="familyFilePath">The full path of the family file (*.rfa).</param>
    /// <returns>
    ///     The metadata as stored in the family file, or <c>null</c> if the file cannot be read, is not
    ///     a compound file, or has no readable <c>PartAtom</c> stream.
    /// </returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static PartAtomInfo? Read(string familyFilePath)
    {
        if (!File.Exists(familyFilePath))
        {
            throw new FileNotFoundException($"The specified family file was not found: {familyFilePath}");
        }

        using var stream = new FileStream(familyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream);
    }

    /// <summary>
    ///     Reads the PartAtom metadata from a stream containing the family file content.
    /// </summary>
    /// <param name="familyStream">A readable, seekable stream positioned anywhere within the family file content.</param>
    /// <returns>
    ///     The metadata, or <c>null</c> if the content is not a compound file or has no readable
    ///     <c>PartAtom</c> stream.
    /// </returns>
    /// <remarks>
    ///     The reader is intentionally tolerant: bulk indexing must be able to skip unreadable files, so
    ///     malformed content yields <c>null</c> instead of an exception. The caller decides whether and how
    ///     to report such files.
    /// </remarks>
    public static PartAtomInfo? Read(Stream familyStream)
    {
        try
        {
            familyStream.Position = 0;
            using var root = RootStorage.Open(familyStream, StorageModeFlags.LeaveOpen);

            if (!root.TryOpenStream(PartAtomStreamName, out var partAtom))
            {
                return null;
            }

            using (partAtom)
            {
                var document = XDocument.Load(partAtom);
                return Extract(document);
            }
        }
        catch (OpenMcdf.FileFormatException)
        {
            // Not an OLE compound file (corrupted or a different format altogether).
            return null;
        }
        catch (EndOfStreamException)
        {
            // Truncated compound file.
            return null;
        }
        catch (XmlException)
        {
            // Malformed PartAtom content.
            return null;
        }
    }

    /// <summary>
    ///     Extracts the metadata from a parsed PartAtom document.
    /// </summary>
    /// <param name="document">The parsed PartAtom XML document.</param>
    /// <returns>The extracted metadata.</returns>
    /// <remarks>
    ///     Elements are matched by local name only. Revit has emitted the Atom elements with varying
    ///     namespace declarations across versions, and the document structure itself is stable.
    /// </remarks>
    private static PartAtomInfo Extract(XDocument document)
    {
        var entry = document.Root;

        var title = entry?.Elements()
                          .FirstOrDefault(element => element.Name.LocalName == "title")?
                          .Value.Trim();

        var productVersion = document.Descendants()
                                     .FirstOrDefault(element => element.Name.LocalName == "product-version")?
                                     .Value.Trim();

        DateTime? updated = null;
        var updatedText = entry?.Elements()
                                .FirstOrDefault(element => element.Name.LocalName == "updated")?
                                .Value.Trim();
        if (DateTime.TryParse(updatedText, CultureInfo.InvariantCulture,
                              DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedUpdated))
        {
            updated = parsedUpdated;
        }

        var symbolNames = document.Descendants()
                                  .Where(element => element.Name.LocalName == "part")
                                  .Select(part => part.Elements()
                                                      .FirstOrDefault(child => child.Name.LocalName == "title")?
                                                      .Value.Trim())
                                  .Where(name => !string.IsNullOrEmpty(name))
                                  .Select(name => name!)
                                  .Distinct()
                                  .ToList();

        return new PartAtomInfo
        {
            Title = string.IsNullOrEmpty(title) ? null : title,
            ProductVersion = string.IsNullOrEmpty(productVersion) ? null : productVersion,
            Updated = updated,
            Category = ExtractCategoryTerm(document, CategoryScheme),
            OmniClassNumber = ExtractCategoryTerm(document, OmniClassScheme),
            SymbolNames = symbolNames
        };
    }

    /// <summary>
    ///     Extracts the term of the category element with the specified scheme.
    /// </summary>
    /// <param name="document">The parsed PartAtom XML document.</param>
    /// <param name="scheme">The category scheme to look for.</param>
    /// <returns>The category term, or <c>null</c> if not present.</returns>
    private static string? ExtractCategoryTerm(XDocument document, string scheme)
    {
        return document.Descendants()
                       .Where(element => element.Name.LocalName == "category")
                       .Where(category => category.Elements()
                                                  .Any(child => child.Name.LocalName == "scheme" &&
                                                                child.Value.Trim() == scheme))
                       .Select(category => category.Elements()
                                                   .FirstOrDefault(child => child.Name.LocalName == "term")?
                                                   .Value.Trim())
                       .FirstOrDefault(term => !string.IsNullOrEmpty(term));
    }
}
