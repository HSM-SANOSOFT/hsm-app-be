<!-- gitnexus:start -->
# GitNexus — Code Intelligence

> **STALE INDEX — do not trust.** The index (5011 symbols, 10640 relationships,
> 194 execution flows) was built from the TypeScript monorepo, which was removed
> from the working tree by the .NET rewrite (tag `freeze/typescript-2026-07-27`
> preserves that source). Every symbol, flow, and impact result it returns
> describes deleted code. Do **not** run `impact`/`detect_changes` against it or
> treat its output as current. Re-run `gitnexus analyze` once the .NET solution
> has meaningful C# surface, then restore the workflow rules below.

## Suspended workflow (restore after re-analysis)

- Run impact analysis before editing any symbol; report blast radius.
- Run `detect_changes()` before committing.
- Use `query`/`context` for exploration instead of grepping.
- Never rename symbols with find-and-replace — use `rename`.

## CLI skills

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
