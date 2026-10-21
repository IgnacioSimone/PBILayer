# Historial de cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Las versiones siguen [SemVer](https://semver.org/lang/es/).

## [2.0.0] — 2026-10-18

Versión grande: la app pasa de herramienta externa de un solo uso a una aplicación completa.

**Nuevo nombre: PBI Layer** (antes "PBI Context"). El instalador desinstala solo la versión anterior y las preferencias se copian a la carpeta nueva (`%LOCALAPPDATA%\PBILayer`).

### Agregado
- **Mapa de relaciones** (Relaciones → *Mapa*): las tablas como cajas conectadas, con "1" / "*" en los extremos, flecha hacia donde viaja el filtro y línea punteada en las relaciones inactivas. Al pasar el mouse por una tabla se resaltan sus relaciones; Ctrl + rueda hace zoom.
- Botón **Enviar comentarios o reportar un problema** (menú ⚙ y *Acerca de*), con formularios de GitHub Issues para fallas y mejoras.
- Tarjetas del Resumen con una descripción breve; al presionarlas llevan a su sección.
- Aviso de espera mientras Power BI Desktop abre el archivo, y botón ✕ para borrar de una vez el texto de los buscadores.
- Interfaz y exportaciones (Word, Excel, HTML, CSV) en español e inglés; idioma automático según Windows, con selector en el menú Ayuda (se aplica al reiniciar).
- **Funciona sola**: al abrirla detecta el tablero abierto en Power BI Desktop y lee su modelo, sin pasar por *Herramientas externas*. Al abrir un `.pbix` que no está en Power BI Desktop, lo abre ahí y espera a que cargue.
- **Búsqueda global** (Ctrl+F) sin distinguir acentos, con contador de resultados por sección.
- **Panel de detalle** de medidas, columnas y consultas M: código DAX/M con colores, botón Copiar y dependencias navegables ("depende de" / "usada por").
- **Dependencias entre consultas de Power Query** en *Uso y limpieza*: una tabla auxiliar, parámetro o función que alimenta a una tabla en uso ya no figura como "sin uso".
- **Exportar** a Word (.docx), Excel (.xlsx), página HTML autónoma, CSV (de la vista filtrada o todo en un .zip), además de JSON y Markdown para IA.
- **Tema** claro, oscuro o el de Windows (con alto contraste); la ventana recuerda tamaño, posición y sección.
- Atajos de teclado, avisos no modales, cancelar operaciones largas, archivos recientes, arrastrar y soltar `.pbix` / snapshots `.json`, copiar celdas y filas.
- **Instalador** (Inno Setup) con icono propio, acceso directo en el menú Inicio y registro automático como herramienta externa; ventana **Acerca de** con la versión.
- **Actualizaciones opcionales**: buscar versiones nuevas en GitHub y bajarlas con verificación SHA-256. Está apagado por defecto: es la única conexión de red de la app y solo ocurre si la activás.

### Cambiado (identidad visual)
- Nuevo icono (capas amarillas con barras blancas sobre fondo negro), también en la cinta de *Herramientas externas* de Power BI Desktop, el instalador, el menú Inicio y *Acerca de*. La fuente es `Assets/icon.svg`; `Assets/make-icon.ps1` regenera el `.ico` y el `.png`.
- Paleta en amarillo, blanco y negro (el amarillo del icono) en los temas claro y oscuro; el oscuro es gris carbón, no negro puro. También en los documentos exportados (Word, Excel, HTML).

### Corregido
- *Uso y limpieza* marcaba como "sin uso" medidas que otras fórmulas referencian con el nombre de la tabla delante (`Tabla[Medida]`).
- Las tarjetas del Resumen fallaban al animarse con un modelo cargado.

### Cambiado
- Reescritura de la interfaz a MVVM (la ventana principal pasó de ~735 líneas de código a un esquema por secciones) y 200+ pruebas automáticas.
- Los archivos exportados se nombran con el nombre del tablero y no con el GUID de la base.

## [1.0.0] — 2026-09-12

Primera versión: lectura del modelo vía DMV (solo metadata), parser de visuales del `.pbix` (PBIR y Layout clásico), detección de objetos sin uso, exportación a Markdown para IA, enmascarado de datos sensibles.
