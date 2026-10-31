using Gadfly.Cli;

// Legion takes Gadfly's keys from MindAttic Vault: its assignment on Vault's Keys tab, else the shared order.
MindAttic.Legion.MindAtticCredentialStore.AppId = "Gadfly";

return await GadflyCli.RunAsync(args);
