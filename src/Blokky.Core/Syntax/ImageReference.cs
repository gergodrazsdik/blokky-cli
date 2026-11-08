using Blokky.Core.Ast;

namespace Blokky.Core.Syntax;

/// <summary>
/// A container image reference in the OCI grammar <c>[registry/]name[:tag][@digest]</c> (syntax.md §6.10, SY-103).
/// </summary>
/// <param name="Registry">The registry host with its optional port, or null.</param>
/// <param name="Name">The repository path.</param>
/// <param name="Tag">The tag, or null.</param>
/// <param name="Digest">The digest (<c>algorithm:hex</c>), or null.</param>
public sealed record ImageReference(string? Registry, string Name, string? Tag, string? Digest)
{
    private const int MaxNameLength = 255;
    private const int MaxTagLength = 128;

    /// <summary>Parses <paramref name="text"/>; null with <paramref name="error"/> (SY-103) if it isn't a reference.</summary>
    public static ImageReference? Parse(string text, out GrammarError? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        error = null;
        var rest = text;
        string? digest = null;
        var at = rest.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            digest = rest[(at + 1)..];
            rest = rest[..at];
            if (DigestProblem(digest) is { } why)
            {
                error = Error(text, why);
                return null;
            }
        }

        string? tag = null;
        var colon = rest.LastIndexOf(':');
        if (colon > rest.LastIndexOf('/'))
        {
            tag = rest[(colon + 1)..];
            rest = rest[..colon];
            if (!IsTag(tag))
            {
                error = Error(text, $"the tag '{tag}' isn't [A-Za-z0-9_][A-Za-z0-9._-]{{0,127}}");
                return null;
            }
        }

        string? registry = null;
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            var first = rest[..slash];
            if (first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal) || first == "localhost")
            {
                registry = first;
                if (!IsRegistry(registry))
                {
                    error = Error(text, $"the registry '{registry}' isn't a host name or IPv4 address with an optional :port");
                    return null;
                }
            }
        }

        var name = registry is null ? rest : rest[(registry.Length + 1)..];
        if (name.Length == 0 || !name.Split('/').All(IsNameComponent))
        {
            error = Error(text, $"the name '{name}' isn't lowercase path components ([a-z0-9] separated by '.', '_', '__' or '-')");
            return null;
        }

        if (rest.Length > MaxNameLength)
        {
            error = Error(text, $"the name is longer than {MaxNameLength} characters");
            return null;
        }

        return new ImageReference(registry, name, tag, digest);
    }

    private static GrammarError Error(string text, string why) =>
        new("SY-103", $"'{text}' isn't an image reference [registry/]name[:tag][@digest]: {why}");

    private static string? DigestProblem(string digest)
    {
        var colon = digest.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return "a digest is algorithm:hex (sha256:…)";
        }

        var algorithm = digest[..colon];
        var hex = digest[(colon + 1)..];
        if (!IsAlgorithm(algorithm))
        {
            return $"the digest algorithm '{algorithm}' isn't [a-z0-9]+([+._-][a-z0-9]+)*";
        }

        if (hex.Length < 32 || hex.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')))
        {
            return "the digest's hex part is at least 32 lowercase hex digits";
        }

        return algorithm == "sha256" && hex.Length != 64 ? "a sha256 digest has 64 hex digits" : null;
    }

    private static bool IsLowerAlnum(char c) => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c);

    /// <summary><c>[a-z0-9]+([+._-][a-z0-9]+)*</c>.</summary>
    private static bool IsAlgorithm(string s)
    {
        var previousSeparator = true;
        foreach (var c in s)
        {
            if (IsLowerAlnum(c))
            {
                previousSeparator = false;
            }
            else if (c is '+' or '.' or '_' or '-' && !previousSeparator)
            {
                previousSeparator = true;
            }
            else
            {
                return false;
            }
        }

        return !previousSeparator;
    }

    /// <summary><c>[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*</c>.</summary>
    private static bool IsNameComponent(string s)
    {
        var i = 0;
        while (true)
        {
            var start = i;
            while (i < s.Length && IsLowerAlnum(s[i]))
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }

            if (i == s.Length)
            {
                return true;
            }

            switch (s[i])
            {
                case '.':
                    i++;
                    break;
                case '_':
                    i += (i + 1 < s.Length && s[i + 1] == '_') ? 2 : 1;
                    break;
                case '-':
                    while (i < s.Length && s[i] == '-')
                    {
                        i++;
                    }

                    break;
                default:
                    return false;
            }
        }
    }

    /// <summary><c>[A-Za-z0-9_][A-Za-z0-9._-]{0,127}</c>.</summary>
    private static bool IsTag(string s) =>
        s.Length is >= 1 and <= MaxTagLength && (char.IsAsciiLetterOrDigit(s[0]) || s[0] == '_')
        && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>
    /// A host name (or IPv4 address) of dot-separated <c>[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?</c>, then an optional
    /// decimal port.
    /// </summary>
    private static bool IsRegistry(string s)
    {
        var colon = s.IndexOf(':', StringComparison.Ordinal);
        var host = colon < 0 ? s : s[..colon];
        if (colon >= 0)
        {
            var port = s[(colon + 1)..];
            if (port.Length is 0 or > 5 || !port.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return host.Split('.').All(label => label.Length > 0 && char.IsAsciiLetterOrDigit(label[0])
            && char.IsAsciiLetterOrDigit(label[^1]) && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }
}
