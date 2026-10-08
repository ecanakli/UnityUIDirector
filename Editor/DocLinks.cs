using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools
{
    // Links from editor tools into the public documentation.
    internal static class DocLinks
    {
        private const string RepositoryBlobRoot = "https://github.com/ecanakli/UnityUIDirector/blob/";
        private const string FallbackReference = "main";
        private const string DocumentationMenuPath = "Tools/UIDirector/Documentation";

        // The docs of the installed version, so a link matches the code in the project.
        internal static string DocumentationRoot => RootFor(InstalledVersion());

        // A tag "v{version}"; "main" when the version is unknown.
        internal static string RootFor(string version)
        {
            string reference = string.IsNullOrWhiteSpace(version) ? FallbackReference : "v" + version.Trim();
            return RepositoryBlobRoot + reference + "/Documentation~/";
        }

        private static string InstalledVersion()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DocLinks).Assembly);
            return package?.version;
        }

        // The Troubleshooting headings are exactly the id, so the anchor is the lower-case id.
        internal static string For(string diagnosticId)
        {
            if (diagnosticId == null)
                throw new ArgumentNullException(nameof(diagnosticId));

            return DocumentationRoot + "Troubleshooting.md#" + diagnosticId.ToLowerInvariant();
        }

        // Every public const string on DiagnosticIds, sorted by id.
        internal static IReadOnlyList<string> AllDiagnosticIds()
        {
            var ids = new List<string>();
            FieldInfo[] fields = typeof(DiagnosticIds).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                    continue;

                var value = field.GetRawConstantValue() as string;
                if (!string.IsNullOrEmpty(value))
                    ids.Add(value);
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        [MenuItem(DocumentationMenuPath)]
        private static void OpenDocumentation()
        {
            Application.OpenURL(DocumentationRoot + "index.md");
        }
    }
}
