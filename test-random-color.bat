@echo off
setlocal

for /f %%c in ('powershell -NoProfile -Command "$r=Get-Random -Minimum 0 -Maximum 256; $g=Get-Random -Minimum 0 -Maximum 256; $b=Get-Random -Minimum 0 -Maximum 256; ('#' + ('{0:X2}{1:X2}{2:X2}' -f $r,$g,$b))"') do set COLOR=%%c

echo Using color %COLOR%
dotnet run --project .\CherryKeyLayout -- --mode static --color "%COLOR%" --brightness full

endlocal
