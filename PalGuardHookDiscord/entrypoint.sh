#!/bin/sh
mount -t cifs -o username=felli,password=Unreal05 //192.168.100.73/palguard /mnt/palguard
exec dotnet PalGuardHookDiscord.dll