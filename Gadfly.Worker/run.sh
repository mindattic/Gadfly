#!/bin/sh
# Linux App Service (Kudu) cannot run run.cmd; this is its equivalent (GAD-T774).
exec dotnet "$(dirname "$0")/Gadfly.Worker.dll" "$@"
