docs: https://github.com/K3nD4rk-Code-Developer/Wave-Websocket-Execution/blob/89c4b150cab3ee0b02f2c15d2fc7b5a803a0c2b5/README.md
full example, etc: https://github.com/K3nD4rk-Code-Developer/Wave-Websocket-Execution/

## Quick start (Windows)
Use the helper script to restore, build, and run the WPF app in one go:

```
# From the repo root
pwsh ./build-and-run.ps1 -Configuration Release
```

The script requires the .NET 6 SDK and PowerShell. It will stop on the first failure and surface the `dotnet` CLI exit code.
