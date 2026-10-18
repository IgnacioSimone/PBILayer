# PBI Context (PBIExplorer)

App WPF (.NET 8, `net8.0-windows`) que documenta modelos de Power BI. Solo lee metadata (DMVs de ADOMD, nunca EVALUATE) y el .pbix.

## Comandos
- Compilar: `dotnet build PBIExplorer.csproj`
- Tests: `dotnet test PBIExplorer.Tests` (xUnit; fijan cultura es-AR en `TestCulture.cs`)
- Textos: editar `Resources/strings/*.tsv` (clave, español, inglés) y correr `powershell -File Resources\generate-resx.ps1`. No editar los `.resx` a mano.
- Íconos: `Assets/icon.svg` es la fuente; `powershell -File Assets\make-icon.ps1` regenera `.ico` y `icon-256.png` (la geometría está duplicada en el script).
- Instalador: `powershell -File build-installer.ps1` → `installer_output/` (Inno Setup, pide admin al instalar).

## Arquitectura
- MVVM con CommunityToolkit.Mvvm. ViewModels en `ViewModels/`, vistas en `Views/` (DataTemplates por ViewModel).
- Servicios en `Services/` (lectura, análisis de uso, exportadores Word/Excel/HTML/CSV/MD, actualizaciones).
- Localización: `Loc.T("Clave")` / `Loc.F("Clave", args)` en código, `{views:Loc Clave}` en XAML. Idioma fijo desde el arranque (`Loc.Initialize`); cambiarlo requiere reiniciar.
- Los valores de dominio (Medida, Columna, "Único sentido"…) se guardan en español canónico; se muestran con `Terms.Display` / propiedades `*Text`.
- Temas: pinceles con `DynamicResource` (ThemeService los reemplaza en runtime).

## Cuidados
- Los archivos con íconos Segoe (caracteres PUA) en `MainViewModel.cs`/`SectionViewModel.cs`: editar solo con reemplazos puntuales, no reescribir enteros.
- Un test comprueba que toda clave usada en código/XAML exista en ambos idiomas.
- Sin firma de código: SmartScreen avisa al instalar.
- El auto-update usa GitHub releases públicas (con repo privado responde "sin releases").
