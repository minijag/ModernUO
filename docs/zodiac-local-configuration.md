# Zodiac local configuration

The machine-specific `Distribution/Configuration/modernuo.json` remains untracked.
For local testing, its `settings` object uses:

```json
{
  "accountHandler.enableAutoAccountCreation": "True",
  "accountHandler.maxAccountsPerIP": "20"
}
```

Restart the shard after changing these settings. Account creation still requires
a previously unused valid username and password. Existing accounts retain their
passwords. Saves, account exports, and generated client MUL/IDX archives are not
part of this source checkpoint.
