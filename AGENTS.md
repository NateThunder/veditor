# AGENT.md

## Purpose

This repository uses a general-purpose commenting and code-structure policy for all code changes made by Codex or any other coding agent.

The goal is to keep code readable, maintainable, and easy to navigate across languages, including C#, .NET, Python, JavaScript, TypeScript, Java, Go, and similar languages.

This policy applies to:
- new code
- refactors
- bug fixes
- feature work
- file moves
- method extraction
- rewrites that preserve behaviour

---

## Core Rules

1. Always add and preserve useful section comments in code.
2. Use banner-style section comments to mark meaningful logic areas.
3. Do not remove comments unless the code they describe is removed.
4. If code is moved, move the related comments with it.
5. If code is rewritten, preserve the meaning and structure of the comments.
6. New logic should be placed under a clearly labeled section comment when the block controls meaningful behaviour.
7. Comment meaningful control areas such as:
   - input handling
   - validation
   - normalization
   - business rules
   - state changes
   - persistence
   - external calls
   - error handling
   - output shaping
   - cleanup
8. Do not add useless comments that only restate obvious syntax.
9. Keep section names stable unless there is a real structural reason to rename them.
10. Prefer readable structure over overly compact code.

---

## Comment Style

Use banner-style section comments that fit the language.

### C# / Java / JavaScript / TypeScript / Go / similar

```csharp
//== input validation =========================================================
if (request == null)
{
    throw new ArgumentNullException(nameof(request));
}
//=============================================================================
```

```csharp
//== state transition: pending -> active ======================================
account.Status = AccountStatus.Active;
account.ActivatedAtUtc = clock.UtcNow;
//=============================================================================
```

### Python / shell / Ruby / YAML-like commented code areas

```python
#== input validation ==========================================================
if request is None:
    raise ValueError("request is required")
#==============================================================================
```

Section comments should:
- start with a clear banner marker such as `//==` or `#==`
- use a short, specific label
- describe responsibility, intent, or control flow
- end with a closing divider line when the block ends

---

## Comment Preservation Policy

Agents must preserve comments aggressively.

### Never remove comments when:
- refactoring code without deleting the related behaviour
- renaming variables, methods, classes, modules, or files
- reordering logic
- extracting helper functions or methods
- converting inline logic into services, handlers, or utilities
- moving code into another file or project
- expanding a short block into a larger implementation

### Remove comments only when:
- the code they describe has been deleted
- the comment is objectively false and cannot be updated accurately
- duplicate comments are created and need to be merged

### When a comment becomes inaccurate:
- update it
- do not delete it without replacement unless the underlying code is gone

---

## General Code Structure Schema

Use this as the default mental model for organising code.

```text
                               input from user / api / cli / file / event
                                                  |
                                                  v
    +----------------------+     +----------------------+     +----------------------+
    |     entry point      | --> |      validation      | --> |    normalization     |
    | handler / command    |     | guards / checks      |     | parse / map / clean  |
    +----------------------+     +----------------------+     +----------------------+
                                                  |
                                                  v
    +----------------------+     +----------------------+     +----------------------+
    |    business rules    | --> |    state changes     | --> |     side effects     |
    | decisions / flow     |     | flags / updates      |     | db / fs / api / io   |
    +----------------------+     +----------------------+     +----------------------+
                                                  |
                                                  v
    +----------------------+     +----------------------+     +----------------------+
    |   output shaping     | --> |    error handling    | --> |    return / emit     |
    | result / dto / map   |     | logs / fallback      |     | response / message   |
    +----------------------+     +----------------------+     +----------------------+
```

This schema is not language-specific. It is the default structure agents should follow when possible.

When `README.md` contains a `General Code Structure Schema` section, that section must:
- include a repository-specific ASCII block diagram for the current system
- put the plain-language explanation first in each block so a non-expert can follow the flow quickly
- place the exact technical labels directly under the plain-language explanation, such as the real file, class, method, handler, or module names
- draw the concrete architecture, control flow, and major runtime paths of the current codebase rather than only restating the generic schema labels
- include a repository-specific mapping from the schema elements to the concrete files, classes, methods, modules, or handlers that implement them
- not satisfy this requirement with only a generic schema plus a lookup table
- avoid diagrams that are technically correct but too jargon-heavy for a human reader to follow quickly
- be updated when those code links change so the documentation stays aligned with the implementation

---

## Documentation Link Policy

Agents must keep repository documentation easy to navigate.

When updating `README.md` or other Markdown documentation:
- use Markdown links when referring to repository files or documentation sections
- prefer stable heading links and file links for long-lived references
- use GitHub-style line anchors such as `./Program.cs#L9` or `./Form1.cs#L1171-L1344` when a specific method, line, or block is the point of reference
- refresh or remove line anchors when code moves so links do not silently point at the wrong location
- if a renderer may not support line anchors, pair the link with the file name and the method or section name in the surrounding text
- keep linked heading names stable unless there is a real structural reason to rename them

---

## UI Design Specification

When working on UI in this repository, agents must behave as expert psychological UI designers, not only as implementers of controls.

The goal is to make the interface feel clear, calm, readable, and trustworthy under real usage, especially in dense desktop layouts.

### Psychological UI design rules

- design for low cognitive load first
- create clear visual hierarchy so the user can tell what is primary, secondary, and informational at a glance
- group related controls so the screen feels intentional rather than crowded
- use spacing, alignment, contrast, and sizing to reduce confusion and hesitation
- prefer readable, stable layouts over visually cramped layouts that fit more controls at the cost of clarity
- make important actions look obviously actionable and supporting information look clearly supportive

### Box and control sizing rules

- boxes, panels, buttons, labels, tabs, group areas, and other text containers must be sized for their real content
- text must not be cut off, half-hidden, clipped, or visually squeezed inside boxes
- do not rely on a box size that only works for short placeholder text
- size controls using the longest realistic text they may need to display
- include enough internal padding so text does not touch edges or look cramped
- prefer layouts that expand, wrap, or reflow content instead of truncating important words
- avoid fixed widths or heights when content length, font size, DPI scaling, or localization can change
- when a fixed-size area is necessary, validate that it still fits the intended text comfortably
- status text, headings, button labels, combo-box items, and dialog actions must remain readable at common Windows scaling settings

### Text-fitting requirements

- check for clipping at minimum on normal scale and larger desktop scaling where text commonly grows
- if text can vary, use measurement-aware sizing, wrapping, multiline layout, or larger containers
- do not accept a layout where words are cut off inside the middle of a button, label, panel, or summary box
- ellipsis may be used only for clearly secondary overflow cases, not as the default solution for primary UI text
- if the UI currently clips text, fixing the sizing and layout takes priority over cosmetic refinements

### Text visibility and contrast requirements

- all visible text must remain clearly readable against its background
- do not place dark text on similarly dark surfaces or light text on similarly light surfaces
- choose foreground and background colors with strong practical contrast, not barely acceptable contrast
- headings, labels, button text, status text, and disabled-state text must still be readable in the actual UI theme
- when using tinted panels, gradients, overlays, accent fills, or custom cards, verify the text still stands out clearly
- do not rely on color alone when contrast, hierarchy, weight, spacing, or borders are needed to preserve readability
- if a background treatment makes text harder to read, adjust the background, foreground, or container styling until the text is clearly visible

### Review standard for UI changes

When making or reviewing UI changes, agents must explicitly check:
- whether any visible text is clipped
- whether text is clearly readable against its background
- whether button and box sizes match their content
- whether spacing and grouping reduce visual stress
- whether the screen remains readable without forcing the user to guess hidden text
- whether the layout still works when labels become slightly longer than expected

### Screenshot reconstruction and photo generator fallback

- when implementing UI from a screenshot, first reproduce the design with the real controls, layout, styling, and assets available in the current stack
- if a visual element from the screenshot does not exist in the current stack and cannot be recreated faithfully with reasonable effort, the agent may use a photo generator or image generator to create a supporting asset
- generated assets must be limited to non-interactive visual content such as photos, illustrations, textures, or decorative background elements
- do not replace real controls, readable interface text, or semantically important UI elements with generated bitmap content
- any generated asset must still follow the repository rules for text visibility, contrast, spacing, sizing, and low-cognitive-load layout
- if a generated asset is used, place it so it supports the UI rather than disguising missing interaction or hiding layout problems

---

## Preferred Section Names

When relevant, prefer short stable labels such as:
- `entry point`
- `input collection`
- `input validation`
- `normalization`
- `configuration load`
- `authorization`
- `precondition checks`
- `business rules`
- `state transition`
- `entity updates`
- `persistence`
- `external service call`
- `output shaping`
- `error handling`
- `logging`
- `cleanup`

Keep names short, stable, and responsibility-based.

---

## Example C# Style

```csharp
public async Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
{
    //== input validation =====================================================
    if (request == null)
    {
        throw new ArgumentNullException(nameof(request));
    }

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Result<UserDto>.Failure("Email is required.");
    }
    //=========================================================================

    //== normalization ========================================================
    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    //=========================================================================

    //== business rules =======================================================
    var existingUser = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);

    if (existingUser != null)
    {
        return Result<UserDto>.Failure("A user with this email already exists.");
    }
    //=========================================================================

    //== entity updates =======================================================
    var user = new User
    {
        Email = normalizedEmail,
        DisplayName = request.DisplayName?.Trim()
    };
    //=========================================================================

    //== persistence ==========================================================
    await _userRepository.AddAsync(user, cancellationToken);
    await _unitOfWork.SaveChangesAsync(cancellationToken);
    //=========================================================================

    //== output shaping =======================================================
    var dto = new UserDto
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName
    };
    //=========================================================================

    //== return response ======================================================
    return Result<UserDto>.Success(dto);
    //=========================================================================
}
```

---

## Example Python Style

```python
def create_user(payload):
    #== input validation ======================================================
    if payload is None:
        raise ValueError("payload is required")

    if "email" not in payload:
        raise ValueError("email is required")
    #==========================================================================

    #== normalization =========================================================
    normalized_email = payload["email"].strip().lower()
    #==========================================================================

    #== business rules ========================================================
    if user_exists(normalized_email):
        return {"ok": False, "error": "user already exists"}
    #==========================================================================

    #== state changes =========================================================
    user = save_user(normalized_email)
    #==========================================================================

    #== output shaping ========================================================
    return {"ok": True, "id": user.id, "email": user.email}
    #==========================================================================
```

---

## Final Instruction to Agents

When making any code change in this repository:
- preserve existing useful comments
- add missing section comments where logic would benefit from them
- do not strip comments during cleanup
- prefer explicit, readable structure over compact but unclear code
- follow the schema and section naming conventions in this file
- apply the UI design specification when changing forms, controls, spacing, box sizing, or text layout
- use the screenshot reconstruction fallback rule when matching a screenshot that includes elements not available in the current stack
- ensure visible UI text fits its container cleanly and is not clipped inside boxes or controls
- ensure visible UI text has clear contrast against its background in the actual rendered interface
- keep Markdown file, section, and line links accurate when documentation references concrete code
- write ASCII schema diagrams so the plain-English meaning comes first and the technical terms stay visible underneath
- keep the `README.md` schema section updated when the current system architecture, major control flow, schema elements, or their code mappings change
