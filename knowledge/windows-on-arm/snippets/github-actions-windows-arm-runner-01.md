# GitHub Actions `windows-11-arm` runner and `actions/setup-python` on ARM64

GitHub Actions provides `windows-11-arm` as a hosted runner label for Windows on Arm CI jobs. It is a separate runner pool from `windows-latest` (which is x64), with its own quota, concurrency limits, and preinstalled software list.

## Enabling ARM64 CI for a Python repo

The minimum viable ARM64 CI job:

```yaml
jobs:
  test-arm64:
    runs-on: windows-11-arm
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: "3.12"
          architecture: arm64
      - run: |
          python -m pip install --upgrade pip
          pip install -r requirements.txt
      - run: pytest
```

Notes on this shape:

- `actions/setup-python` supports `architecture: arm64` for recent CPython minor versions. Older minors (roughly pre-3.11) may not have prebuilt ARM64 CPython builds available through this action and will either fall back to x64-under-emulation or fail to resolve — pin a supported minor.
- `runs-on: windows-11-arm` counts against a separate concurrency budget from `windows-latest`. A matrix that fans out across both must respect both budgets; a repo without ARM64 runner allocation will queue indefinitely.
- Preinstalled software on `windows-11-arm` is not identical to `windows-latest`. Do not assume `msbuild`, specific Visual Studio workloads, or specific `.NET` SDKs are present without checking the runner's `imagegeneration` manifest.

## Matrix pattern (x64 and ARM64 in one job)

```yaml
jobs:
  test:
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, windows-11-arm]
        python-version: ["3.11", "3.12"]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: ${{ matrix.python-version }}
          architecture: ${{ matrix.os == 'windows-11-arm' && 'arm64' || 'x64' }}
      - run: pip install -r requirements.txt
      - run: pytest
```

`fail-fast: false` is important during migration so an ARM64 failure does not cancel the passing x64 legs — you want the diff to be visible.

## Common failure modes

- **Runner not available for the account.** `windows-11-arm` may be limited by plan or by org policy. Confirm availability before adding a required check that runs on it.
- **`pip` falls back to source build.** The runner-side `pip install` on ARM64 will attempt to compile any package without a `win_arm64` wheel. Without the MSVC Build Tools and, where needed, a Rust toolchain on the image, the install step fails. Either preinstall the toolchain in an earlier step or pin to versions with published `win_arm64` wheels.
- **Cache key collisions.** A pip cache keyed only by `requirements.txt` hash will serve x64 wheels to ARM64 jobs (and vice versa). Include the runner architecture in the cache key: `${{ runner.os }}-${{ runner.arch }}-pip-${{ hashFiles('requirements.txt') }}`.

## See also

- GitHub Actions runners documentation: <https://docs.github.com/en/actions>
