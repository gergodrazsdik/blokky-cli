# Proposed schema changes, applied to a copy for testing (see the report).
.["$defs"].duration += {
  "errorMessage": "A duration is a string with units: 30s, 5m, 1h30m. A bare number needs a unit (SY-57).",
  "patternErrorMessage": "Durations are <integer><unit> parts in h, m, s, ms order: 500ms, 10s, 1h30m. No fractions: 1500ms, not 1.5s (SY-56, SY-57)."
}
| .["$defs"].specVersion = {
  "description": "The spec version the plan targets, MAJOR.MINOR (SY-51).",
  "anyOf": [
    {"type": "string", "pattern": "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$"},
    {"type": "number", "enum": [1], "doNotSuggest": true}
  ],
  "errorMessage": "Write the spec version as MAJOR.MINOR, for example blokky: 1.0 (SY-51)."
}
| .["$defs"].bool.anyOf[1] += {"doNotSuggest": true}
| .["$defs"].input.properties.description = {"$ref": "#/$defs/scalar"}
| .["$defs"].root.properties.fail_fast += {"description": "Stop the other children of all/any/race as soon as one fails. true or false."}
| .["$defs"].root.properties.timeout += {"description": "Wall-clock limit for this node, a duration such as 30s or 10m."}
| .["$defs"].root.properties.exec += {"markdownDescription": "Run a program without a shell. String form `exec: dotnet build -c Release`, or a list `exec: [git, commit, -m, 'fix: typo']`."}
| .["$defs"].root.properties.blokky += {"title": "blokky"}
