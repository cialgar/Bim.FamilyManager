using System.IO;
using System.Xml;
using System.Xml.Linq;
using OpenMcdf;

namespace Bim.FamilyManager.Rfa;

/// <summary>
///     Reads the family category from the <c>PartAtom</c> stream of a Revit family file (*.rfa).
/// </summary>
/// <remarks>
///     Every family file is an OLE compound file whose <c>PartAtom</c> stream contains the same Atom XML
///     document that <c>Document.ExtractPartAtomFromFamilyFile</c> produces. The family category is stored
///     as an Atom <c>category</c> element whose <c>scheme</c> is <c>adsk:revit:grouping</c>.
///     <c>Scotec.Revit.RevitFamily.RevitFamilyInfo</c> parses the same stream but does not expose the
///     category, which the family index requires for filtering; this reader fills exactly that gap.
///     Only the compound file sectors belonging to the <c>PartAtom</c> stream are read; the family file is
///     never loaded into memory as a whole.
/// </remarks>
public static class PartAtomCategoryReader
{
    private const string PartAtomStreamName = "PartAtom";
    private const string CategoryScheme = "adsk:revit:grouping";

    /// <summary>
    ///     Reads the family category from the specified family file.
    /// </summary>
    /// <param name="familyFilePath">The full path of the family file (*.rfa).</param>
    /// <returns>
    ///     The category name as stored in the family file (localized to the language the family was
    ///     authored in), or <c>null</c> if the file cannot be read, is not a compound file, has no
    ///     <c>PartAtom</c> stream, or the stream does not contain a category.
    /// </returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static string? ReadCategory(string familyFilePath)
    {
        if (!File.Exists(familyFilePath))
        {
            throw new FileNotFoundException($"The specified family file was not found: {familyFilePath}");
        }

        using var stream = new FileStream(familyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadCategory(stream);
    }

    /// <summary>
    ///     Reads the family category from a stream containing the family file content.
    /// </summary>
    /// <param name="familyStream">A readable, seekable stream positioned anywhere within the family file content.</param>
    /// <returns>
    ///     The category name, or <c>null</c> if the content is not a compound file, has no
    ///     <c>PartAtom</c> stream, or the stream does not contain a category.
    /// </returns>
    /// <remarks>
    ///     The reader is intentionally tolerant: bulk indexing must be able to skip unreadable files, so
    ///     malformed content yields <c>null</c> instead of an exception. The caller decides whether and how
    ///     to report such files.
    /// </remarks>
    public static string? ReadCategory(Stream familyStream)
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
                return ExtractCategory(document);
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
    ///     Extracts the category from a PartAtom document.
    /// </summary>
    /// <param name="document">The parsed PartAtom XML document.</param>
    /// <returns>The category term whose scheme is <c>adsk:revit:grouping</c>, or <c>null</c> if not present.</returns>
    /// <remarks>
    ///     The comparison uses local names only. Revit has emitted the Atom elements with varying namespace
    ///     declarations across versions, and the category structure itself is stable.
    /// </remarks>
    private static string? ExtractCategory(XDocument document)
    {
        return document.Descendants()
                       .Where(element => element.Name.LocalName == "category")
                       .Where(category => category.Elements()
                                                  .Any(child => child.Name.LocalName == "scheme" &&
                                                                child.Value.Trim() == CategoryScheme))
                       .Select(category => category.Elements()
                                                   .FirstOrDefault(child => child.Name.LocalName == "term")?
                                                   .Value.Trim())
                       .FirstOrDefault(term => !string.IsNullOrEmpty(term));
    }
}
