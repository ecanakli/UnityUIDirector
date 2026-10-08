using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools
{
    // Default rows, then this catalog's validation issues with a link to each Troubleshooting section.
    [CustomEditor(typeof(ViewCatalog))]
    internal sealed class CatalogInspector : Editor
    {
        private readonly List<CatalogIssue> _issues = new List<CatalogIssue>();

        private void OnEnable()
        {
            Revalidate();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                if (GUILayout.Button("Validate", GUILayout.Width(80f)))
                    Revalidate();
            }

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues found.", MessageType.Info);
                return;
            }

            foreach (CatalogIssue issue in _issues)
                DrawIssue(issue);
        }

        private static void DrawIssue(CatalogIssue issue)
        {
            MessageType type = issue.Severity == CatalogIssueSeverity.Error ? MessageType.Error : MessageType.Warning;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(issue.Id + ": " + issue.Message, type);
                if (GUILayout.Button("?", GUILayout.Width(24f), GUILayout.Height(24f)))
                    Application.OpenURL(DocLinks.For(issue.Id));
            }
        }

        // Validates every project catalog, so duplicate ids across catalogs show, then keeps this catalog's issues.
        private void Revalidate()
        {
            _issues.Clear();

            var catalog = target as ViewCatalog;
            if (catalog == null)
                return;

            List<ViewCatalog> catalogs = CatalogValidation.FindProjectCatalogs();
            if (!catalogs.Contains(catalog))
                catalogs.Add(catalog);

            List<CatalogIssue> all = CatalogValidation.Validate(catalogs, CatalogValidation.FindKeyTypes(), CatalogValidation.DefaultSettings());
            foreach (CatalogIssue issue in all)
            {
                if (issue.Catalog == catalog)
                    _issues.Add(issue);
            }
        }
    }
}
