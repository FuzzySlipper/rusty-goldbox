using System.Text;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox authoring</c>: discover and copy the embedded authoring kit.</summary>
internal static class AuthoringCommand
{
    private const string Usage =
        "Usage: goldbox authoring list | show <resource> | copy <resource> --out <dir> [--overwrite] | copy --all --out <dir> [--overwrite]";

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--out"], ["--all", "--overwrite"]);
        if (error is not null)
        {
            return output.UsageError(error + $"\n{Usage}");
        }

        if (parsed.Positionals.Count == 0)
        {
            return output.UsageError(Usage);
        }

        return parsed.Positionals[0].ToLowerInvariant() switch
        {
            "list" => List(parsed, output),
            "show" => Show(parsed, output),
            "copy" => Copy(parsed, output, workingDirectory),
            _ => output.UsageError($"Unknown authoring command '{parsed.Positionals[0]}'. {Usage}"),
        };
    }

    private static int List(Arguments parsed, Output output)
    {
        if (parsed.Positionals.Count != 1 || parsed.All("--out").Count > 0 || parsed.Has("--all") || parsed.Has("--overwrite"))
        {
            return output.UsageError("Usage: goldbox authoring list [--json]");
        }

        if (output.Json)
        {
            output.WriteJson(new
            {
                ok = true,
                revision = "1.0",
                resources = AuthoringKit.Resources.Select(ToJson),
            });
            return GoldboxCli.Ok;
        }

        output.Line("Embedded campaign-authoring resources (revision 1.0):");
        foreach (AuthoringResource resource in AuthoringKit.Resources)
        {
            output.Line($"  {resource.Id,-9} {resource.Title} — {resource.Description}");
        }

        output.Line("Show one with: goldbox authoring show <resource>");
        output.Line("Copy one or all with: goldbox authoring copy <resource> --out <dir>");
        return GoldboxCli.Ok;
    }

    private static int Show(Arguments parsed, Output output)
    {
        if (parsed.Positionals.Count != 2 || parsed.All("--out").Count > 0 || parsed.Has("--all") || parsed.Has("--overwrite"))
        {
            return output.UsageError("Usage: goldbox authoring show <resource> [--json]");
        }

        if (!AuthoringKit.TryGet(parsed.Positionals[1], out AuthoringResource? resource))
        {
            return UnknownResource(parsed.Positionals[1], output);
        }

        if (!AuthoringKit.TryRead(resource!, out string content))
        {
            return ResourceError(resource!, output);
        }

        if (output.Json)
        {
            output.WriteJson(new
            {
                ok = true,
                revision = "1.0",
                resource = ToJson(resource!),
                content,
            });
            return GoldboxCli.Ok;
        }

        output.Line($"{resource!.Title} [{resource.Status}] ({resource.Id})");
        output.Line();
        output.Line(content.TrimEnd());
        return GoldboxCli.Ok;
    }

    private static int Copy(Arguments parsed, Output output, string workingDirectory)
    {
        if (parsed.Positionals.Count > 2)
        {
            return output.UsageError(Usage);
        }

        string? destinationText = parsed.Single("--out");
        if (destinationText is null)
        {
            return output.UsageError($"authoring copy needs --out <dir>. {Usage}");
        }

        bool all = parsed.Has("--all");
        if ((all && parsed.Positionals.Count != 1) || (!all && parsed.Positionals.Count != 2))
        {
            return output.UsageError("Use copy <resource> --out <dir> or copy --all --out <dir>.");
        }

        List<AuthoringResource> resources;
        if (all)
        {
            resources = [.. AuthoringKit.Resources];
        }
        else if (!AuthoringKit.TryGet(parsed.Positionals[1], out AuthoringResource? resource))
        {
            return UnknownResource(parsed.Positionals[1], output);
        }
        else
        {
            resources = [resource!];
        }

        string destination = Path.GetFullPath(destinationText, workingDirectory);
        if (File.Exists(destination))
        {
            return CopyError("authoring.copy.output", $"The copy destination '{destination}' is a file. Pass a directory that can hold the resource files.", destination, output);
        }

        try
        {
            Directory.CreateDirectory(destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CopyError("authoring.copy.output", $"Can't create copy destination '{destination}': {exception.Message}", destination, output);
        }

        List<(AuthoringResource Resource, string Content, string Target)> copies = [];
        foreach (AuthoringResource resource in resources)
        {
            if (!AuthoringKit.TryRead(resource, out string content))
            {
                return ResourceError(resource, output);
            }

            string target = Path.Combine(destination, resource.FileName);
            if (!parsed.Has("--overwrite") && File.Exists(target))
            {
                return CopyError(
                    "authoring.copy.exists",
                    $"Refusing to overwrite '{target}'. Choose another directory or pass --overwrite explicitly.",
                    target,
                    output);
            }

            copies.Add((resource, content, target));
        }

        try
        {
            Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            foreach ((AuthoringResource resource, string content, string target) in copies)
            {
                File.WriteAllText(target, content, utf8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CopyError("authoring.copy.write", $"Can't write the authoring kit to '{destination}': {exception.Message}", destination, output);
        }

        if (output.Json)
        {
            output.WriteJson(new
            {
                ok = true,
                revision = "1.0",
                destination,
                overwritten = parsed.Has("--overwrite"),
                files = copies.Select(copy => new
                {
                    id = copy.Resource.Id,
                    path = copy.Target,
                    bytes = Encoding.UTF8.GetByteCount(copy.Content),
                }),
            });
        }
        else
        {
            output.Line($"Copied {copies.Count} embedded authoring resource{(copies.Count == 1 ? "" : "s")} to {destination}.");
            foreach ((AuthoringResource resource, _, string target) in copies)
            {
                output.Line($"  {resource.Id}: {target}");
            }
        }

        return GoldboxCli.Ok;
    }

    private static object ToJson(AuthoringResource resource)
    {
        return new
        {
            id = resource.Id,
            title = resource.Title,
            description = resource.Description,
            file = resource.FileName,
            status = resource.Status,
        };
    }

    private static int UnknownResource(string id, Output output)
    {
        string choices = string.Join(", ", AuthoringKit.Resources.Select(resource => resource.Id));
        return CopyError("authoring.resource", $"Unknown authoring resource '{id}'. Choose one of: {choices}.", null, output);
    }

    private static int ResourceError(AuthoringResource resource, Output output)
    {
        return CopyError(
            "authoring.resource",
            $"Embedded authoring resource '{resource.Id}' is unavailable in this goldbox build. Rebuild the CLI with its embedded resources.",
            resource.FileName,
            output);
    }

    private static int CopyError(string rule, string message, string? path, Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                ok = false,
                diagnostics = new[]
                {
                    new { rule, message, path },
                },
            });
        }
        else
        {
            output.Line($"error[{rule}] {message}");
        }

        return GoldboxCli.Invalid;
    }
}
