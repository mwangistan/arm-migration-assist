# Windows on Arm Guidance Corpus — Attribution

## Source

The canonical source of Windows on Arm guidance is the Microsoft Learn documentation at:

- <https://learn.microsoft.com/en-us/windows/arm/>

## Prototype content notice

The snippets in this corpus are marked with `producer.sourceKind` in `corpus.json`.

- `hand-authored-prototype` marks snippets that were paraphrased for prototype and hackathon use. They convey publicly known technical facts about Windows on Arm, Arm64EC, and ARM64 porting; they are not verbatim excerpts of Microsoft Learn content.
- `microsoft-learn-snapshot` marks snippets that were produced by a sanctioned corpus builder that snapshots Microsoft Learn pages with attribution.

## Guidance for maintainers

- Do not paste verbatim Microsoft Learn content into snippets tagged as `hand-authored-prototype`. Rewrite in your own words while preserving technical accuracy and the `sourceUrl` for reference.
- When the corpus builder replaces prototype snippets with sanctioned snapshots, update `producer.sourceKind` accordingly and bump `corpusVersion`.
- The `sourceUrl` field must always point to the corresponding Microsoft Learn page so readers can consult the authoritative content directly.

## Refresh policy

- The corpus is refreshed as an offline, maintainer-only task. The planner never triggers a refresh at request time.
- Every refresh bumps `corpusVersion` and is reviewed via pull request.
- Every planner run records the `corpusVersion` and the set of `guidanceId` values retrieved and cited.
