# Cómo contribuir

¡Gracias por el interés en PBI Layer!

## Reportar un problema o proponer una mejora

Usá los formularios de [Issues](https://github.com/IgnacioSimone/PBILayer/issues/new/choose) (o el botón
**Enviar comentarios** del menú ⚙ de la app). Antes de pegar el registro (`debug.log`) o capturas, revisalos:
pueden tener nombres de tableros o rutas de tu equipo.

Las vulnerabilidades de seguridad se reportan en privado: ver [SECURITY.md](SECURITY.md).

## Proponer cambios en el código

1. Abrí primero un Issue contando qué querés cambiar, así lo charlamos antes de que inviertas tiempo.
2. Necesitás Windows, el SDK de .NET 8 y Power BI Desktop para probar con un modelo real.
3. Compilá con `dotnet build PBIExplorer.csproj` y corré los tests con `dotnet test PBIExplorer.Tests`: tienen que pasar todos.
4. Los textos de la interfaz no se escriben en el código: van en `Resources/strings/*.tsv` (clave, español, inglés)
   y se regeneran con `powershell -File Resources\generate-resx.ps1`.
5. Mantené el principio de la app: **solo metadata**. Ningún cambio puede leer filas de datos del modelo.

Al enviar un pull request aceptás que tu aporte se publique bajo la licencia MIT del proyecto.
