using System.IO;
using System.Net;
using System.Text;

namespace PBIExplorer.Services.Export;

/// <summary>Una única página HTML autónoma (sin recursos externos ni conexión a internet): navegación por
/// secciones, buscador que filtra todas las tablas, y tema claro/oscuro según el sistema.</summary>
public static class HtmlExporter
{
    public static void WriteFile(string path, ExportDocument document) =>
        File.WriteAllText(path, Render(document), new UTF8Encoding(false));

    public static string Render(ExportDocument document)
    {
        static string E(string? text) => WebUtility.HtmlEncode(ExportText.StripInvalidXmlChars(text));

        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"es\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        // La página es un documento estático: nada de cargar recursos de afuera.
        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data:\">");
        sb.AppendLine($"<title>{E(document.SourceName)} — Documentación</title>");
        sb.AppendLine("<style>" + Css + "</style></head><body>");

        sb.AppendLine("<header>");
        sb.AppendLine($"<h1>{E(document.SourceName)}</h1>");
        sb.AppendLine($"<p class=\"meta\">Documentación generada el {document.GeneratedAt:dd/MM/yyyy HH:mm} con PBI Context</p>");
        sb.AppendLine($"<p class=\"meta\">{E(ExportText.PrivacyNote)}</p>");
        sb.AppendLine("<div class=\"facts\">");
        foreach (var fact in document.Summary)
            sb.AppendLine($"<div class=\"fact\"><span>{E(fact.Label)}</span><strong>{E(fact.Value)}</strong></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</header><div class=\"bar\">");
        sb.AppendLine("<input id=\"q\" type=\"search\" placeholder=\"Buscar en todas las secciones…\" aria-label=\"Buscar en todas las secciones\">");
        sb.AppendLine("<nav>");
        for (var i = 0; i < document.Tables.Count; i++)
            sb.AppendLine($"<a href=\"#s{i}\">{E(document.Tables[i].Title)} <span class=\"n\" id=\"n{i}\">{document.Tables[i].Rows.Count:N0}</span></a>");
        sb.AppendLine("</nav></div><main>");

        for (var i = 0; i < document.Tables.Count; i++)
        {
            var table = document.Tables[i];
            sb.AppendLine($"<section id=\"s{i}\" data-total=\"{table.Rows.Count}\"><h2>{E(table.Title)}</h2>");
            if (!string.IsNullOrEmpty(table.Note)) sb.AppendLine($"<p class=\"note\">{E(table.Note)}</p>");

            sb.AppendLine("<div class=\"scroll\"><table><thead><tr>");
            foreach (var header in table.Headers) sb.AppendLine($"<th>{E(header)}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var row in table.Rows)
            {
                sb.Append("<tr>");
                for (var c = 0; c < row.Count; c++)
                {
                    var cell = E(row[c]);
                    sb.Append(table.CodeColumns.Contains(c) ? $"<td class=\"code\"><pre>{cell}</pre></td>" : $"<td>{cell}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></div></section>");
        }

        sb.AppendLine("</main><script>" + Script + "</script></body></html>");
        return sb.ToString();
    }

    private const string Css = """
        :root{color-scheme:light dark;--bg:#f5f5f2;--card:#ffffff;--ink:#151616;--dim:#565753;--line:#dcdcd6;--accent:#8a6400;--soft:#fff3c4;--mono:#f1f1ec}
        @media (prefers-color-scheme:dark){:root{--bg:#1f2020;--card:#272828;--ink:#f5f5f2;--dim:#c5c5c0;--line:#3b3c3b;--accent:#f6c944;--soft:#3a3317;--mono:#1c1d1d}}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.5 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif}
        header{padding:28px 32px 0}
        .bar{position:sticky;top:0;z-index:2;background:var(--bg);padding:10px 32px 0;border-bottom:1px solid var(--line)}
        h1{margin:0 0 4px;font-size:26px}h2{margin:0 0 12px;font-size:20px}
        .meta{margin:2px 0;color:var(--dim);font-size:12.5px}
        .facts{display:flex;flex-wrap:wrap;gap:8px;margin:14px 0}
        .fact{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:6px 12px;display:flex;flex-direction:column;min-width:92px}
        .fact span{font-size:11px;color:var(--dim)}.fact strong{font-size:18px}
        #q{width:min(420px,100%);padding:8px 12px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--ink);font:inherit}
        #q:focus{outline:2px solid var(--accent);border-color:transparent}
        nav{display:flex;flex-wrap:wrap;gap:6px;padding:12px 0}
        nav a{color:var(--ink);text-decoration:none;border:1px solid var(--line);background:var(--card);border-radius:999px;padding:3px 12px;font-size:13px}
        nav a:hover,nav a:focus{border-color:var(--accent);color:var(--accent)}
        nav .n{background:var(--soft);border-radius:999px;padding:0 7px;margin-left:4px;font-size:11.5px}
        main{padding:8px 32px 48px}section{margin-top:28px;scroll-margin-top:130px}
        .note{color:var(--dim);font-size:12.5px;margin:-4px 0 10px}
        .scroll{overflow:auto;border:1px solid var(--line);border-radius:12px;background:var(--card);max-height:70vh}
        table{border-collapse:collapse;width:100%}
        th{position:sticky;top:0;background:var(--card);text-align:left;font-size:11px;letter-spacing:.04em;text-transform:uppercase;color:var(--dim);padding:10px 12px;border-bottom:1px solid var(--line)}
        td{padding:8px 12px;border-bottom:1px solid var(--line);vertical-align:top}
        tr:last-child td{border-bottom:0}tbody tr:hover{background:var(--soft)}
        td.code{min-width:320px;max-width:560px}
        pre{margin:0;font:12px/1.45 "Cascadia Mono",Consolas,monospace;white-space:pre-wrap;word-break:break-word;background:var(--mono);padding:6px 8px;border-radius:6px}
        .hidden{display:none}
        @media print{.bar{position:static}.scroll{max-height:none;overflow:visible}#q{display:none}}
        """;

    private const string Script = """
        (function(){
          var q=document.getElementById('q');
          function norm(s){return s.normalize('NFD').replace(/[̀-ͯ]/g,'').toLowerCase();}
          function run(){
            var t=norm(q.value.trim());
            document.querySelectorAll('section').forEach(function(sec,i){
              var rows=sec.querySelectorAll('tbody tr'),shown=0;
              rows.forEach(function(r){var ok=!t||norm(r.textContent).indexOf(t)>-1;r.classList.toggle('hidden',!ok);if(ok)shown++;});
              var n=document.getElementById('n'+i);if(n)n.textContent=t?shown+' de '+rows.length:rows.length;
            });
          }
          q.addEventListener('input',run);
        })();
        """;
}
