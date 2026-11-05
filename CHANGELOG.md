# Changelog

All notable changes to blokky-cli are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Each release also states which spec versions of
[blokky-specs](https://github.com/gergodrazsdik/blokky-specs) it supports.

## [Unreleased]

### Added

- Runtime contracts moved from blokky-specs ([ADR 0006](docs/adr/0006-phase-order-and-incremental-cli.md)): `docs/contracts/output-contract.md`, `docs/contracts/leaves-and-validation.md`, `docs/contracts/errors.md` (CLI-only error codes and the phase → exit-code mapping), and the report, events, `leaves` and validator schemas in `schema/`.
- `docs/contracts/run-control.md`: run lifecycle, process control, run budget, cleanup deadline, shutdown and privilege mechanics, moved from the blokky-specs execution model, containers and shells docs (former rule IDs kept in brackets); the script heuristics now live in `leaves-and-validation.md` (LV-16).
