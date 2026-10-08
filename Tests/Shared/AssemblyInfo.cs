using System.Runtime.CompilerServices;

// The fakes stay internal; the test assemblies see them through these attributes.
[assembly: InternalsVisibleTo("Ecanakli.UIDirector.Tests")]
[assembly: InternalsVisibleTo("Ecanakli.UIDirector.PlayModeTests")]
[assembly: InternalsVisibleTo("Ecanakli.UIDirector.DOTween.Tests")]
[assembly: InternalsVisibleTo("Ecanakli.UIDirector.Addressables.Tests")]
[assembly: InternalsVisibleTo("Ecanakli.UIDirector.DependencyInjection.Zenject.Tests")]
[assembly: InternalsVisibleTo("Ecanakli.UIDirector.Editor.Tests")]
