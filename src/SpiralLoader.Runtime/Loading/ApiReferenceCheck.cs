using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SpiralLoader.Runtime.Loading;

/// <summary>
/// Reads which version of an assembly a compiled mod was built against, from its metadata, without loading it.
/// </summary>
internal static class ApiReferenceCheck {

    /// <summary>
    /// The version of <paramref name="referencedAssembly"/> that the assembly at <paramref name="path"/> references,
    /// or null if it does not reference it or the file is not a readable managed assembly (loading it then
    /// reports the real problem).
    /// </summary>
    public static Version? ReadReferencedVersion(string path, string referencedAssembly) {
        try {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            if (!reader.HasMetadata) {
                return null;
            }

            var metadata = reader.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences) {
                var reference = metadata.GetAssemblyReference(handle);
                if (metadata.StringComparer.Equals(reference.Name, referencedAssembly)) {
                    return reference.Version;
                }
            }
            return null;
        } catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException) {
            return null;
        }
    }

}
