# Seguridad

## Cómo reportar una vulnerabilidad

Si encontraste un problema de seguridad en PBI Context, **no lo publiques en un issue**.
Usá el botón **Report a vulnerability** de la pestaña *Security* de este repositorio
(reporte privado de GitHub). Respondo lo antes posible.

## Qué hace (y qué no hace) la aplicación

- Solo lee **metadata** del modelo y del reporte (nombres, fórmulas, relaciones). Nunca consulta filas de datos.
- No envía información a ningún servidor. La única conexión de red es la búsqueda de actualizaciones en las
  releases de GitHub de este repositorio. Está apagada por defecto: solo se activa si la elegís (se cambia en *Acerca de*).
- Las exportaciones enmascaran correos, documentos de identidad, claves y rutas locales.

## Verificar la descarga

Cada release incluye el instalador y un archivo `.sha256`. Comprobá que la huella coincida:

```powershell
Get-FileHash .\PBIContext-Setup-X.Y.Z.exe -Algorithm SHA256
```

El instalador todavía no está firmado digitalmente, por eso Windows SmartScreen puede mostrar un aviso.
