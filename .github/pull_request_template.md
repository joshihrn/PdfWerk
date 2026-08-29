## What this changes

<!-- What the change does, and why. The "why" is the part reviewers cannot reconstruct. -->

## Related issue

<!-- e.g. Fixes #12. For anything beyond a small fix, please open an issue first. -->

## Checklist

- [ ] `dotnet test` passes
- [ ] A bug fix includes a test that fails without it
- [ ] Any new dependency is MIT, Apache-2.0 or BSD, and is recorded in `THIRD-PARTY-NOTICES.md`
- [ ] New endpoints run through `ActionRunner`, so quota enforcement applies
- [ ] Anything touching file parsing has a case in `HardeningTests`

## Contributor Licence Agreement

- [ ] **I have read the [CLA](../CLA.md) and I agree to it for this contribution.**

The CLA is required because PdfWerk is BSL-licensed and converts to Apache-2.0 on its Change Date;
without it, the project could not be relicensed or converted. You keep full ownership of your work
— see [CONTRIBUTING.md](../CONTRIBUTING.md) for why this exists.
