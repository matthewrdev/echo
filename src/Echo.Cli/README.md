# Echo.Cli

Implements the `echo-photo` command line. Feature commands live under `Commands/`. Each command owns its usage text and delegates project and processing work to the corresponding subsystem.

Use `echo-photo --help` for command groups and `echo-photo images metadata --help` for a specific action. Project commands accept both `echo-photo --project PATH images list` and `echo-photo project PATH images list`.
