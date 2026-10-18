<#
.SYNOPSIS
    Genera Strings.resx (español, idioma base) y Strings.en.resx (inglés) a partir de Resources\strings\*.tsv.

.DESCRIPTION
    Los textos de la aplicación se editan en los .tsv (una línea por texto: clave <TAB> español <TAB> inglés) porque
    son más fáciles de revisar y de traducir que el XML de un .resx. Este script los une, valida y escribe los .resx
    que usa la aplicación (no los edites a mano: se pisan en la próxima corrida).

    Formato de cada línea:   Clave<TAB>Texto en español<TAB>Text in English
    - Una línea que empieza con # es un comentario.
    - Dentro del texto, \n es un salto de línea, \t un tabulador y \\ una barra invertida.
    - {0}, {1}… son valores que pone el programa; tienen que ser los mismos en los dos idiomas.
    - Para escribir una llave literal usá {{ y }}.

    Uso:  powershell -File Resources\generate-resx.ps1
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding($false)

function Unescape([string]$text) {
    $sb = New-Object Text.StringBuilder
    for ($i = 0; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ($c -eq '\' -and $i + 1 -lt $text.Length) {
            $n = $text[$i + 1]
            switch ($n) {
                'n' { [void]$sb.Append("`n"); $i++; continue }
                't' { [void]$sb.Append("`t"); $i++; continue }
                '\' { [void]$sb.Append('\'); $i++; continue }
            }
        }
        [void]$sb.Append($c)
    }
    return $sb.ToString()
}

function Placeholders([string]$text) {
    # {0}, {1}… (ignorando las llaves escapadas {{ y }})
    $clean = $text.Replace('{{', '').Replace('}}', '')
    return @([regex]::Matches($clean, '\{(\d+)(?:[:,][^}]*)?\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

$entries = [ordered]@{}
$errors = @()
foreach ($file in Get-ChildItem (Join-Path $here 'strings') -Filter *.tsv | Sort-Object Name) {
    $n = 0
    foreach ($line in [IO.File]::ReadAllLines($file.FullName, $utf8)) {
        $n++
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
        $parts = $line.Split("`t")
        if ($parts.Count -ne 3) { $errors += "$($file.Name):$n tiene $($parts.Count) columnas (se esperaban 3: clave, español, inglés)"; continue }
        $key = $parts[0].Trim(); $es = Unescape $parts[1]; $en = Unescape $parts[2]
        if ($key -notmatch '^[A-Za-z][A-Za-z0-9_]*$') { $errors += "$($file.Name):$n clave inválida '$key'"; continue }
        if ($entries.Contains($key)) { $errors += "$($file.Name):$n clave repetida '$key'"; continue }
        if ([string]::IsNullOrWhiteSpace($es) -or [string]::IsNullOrWhiteSpace($en)) { $errors += "$($file.Name):$n '$key' tiene un idioma vacío"; continue }
        if (((Placeholders $es) -join ',') -ne ((Placeholders $en) -join ',')) { $errors += "$($file.Name):$n '$key' usa distintos {valores} en español e inglés" }
        $entries[$key] = @($es, $en)
    }
}
if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }; throw "Hay $($errors.Count) problema(s) en los .tsv; no se generó nada." }

function Write-Resx([string]$path, [int]$index) {
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine('<!-- GENERADO por Resources\generate-resx.ps1 a partir de Resources\strings\*.tsv. No editar a mano. -->')
    [void]$sb.AppendLine('<root>')
    [void]$sb.AppendLine('  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>')
    [void]$sb.AppendLine('  <resheader name="version"><value>2.0</value></resheader>')
    [void]$sb.AppendLine('  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>')
    [void]$sb.AppendLine('  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>')
    foreach ($key in $entries.Keys) {
        $value = [Security.SecurityElement]::Escape($entries[$key][$index])
        [void]$sb.AppendLine("  <data name=""$key"" xml:space=""preserve""><value>$value</value></data>")
    }
    [void]$sb.AppendLine('</root>')
    [IO.File]::WriteAllText($path, $sb.ToString(), $utf8)
}

Write-Resx (Join-Path $here 'Strings.resx') 0
Write-Resx (Join-Path $here 'Strings.en.resx') 1
"Generados Strings.resx y Strings.en.resx con $($entries.Count) textos."
