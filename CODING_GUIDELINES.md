# Coding Conventions

NOTE:

> This file is a snapshot of the BigRedProf coding standards.
> The canonical version lives in bigredprof/foundation.
> If you modify this file, consider updating the canonical copy as well.

## Indentation & Spacing

* Always use **tabs** (never spaces) for indentation.
* Keep indentation consistent across all files.

## Line Endings

* Use **CRLF** line endings for Windows-oriented files, including C# and .NET project files, PowerShell scripts, and Windows command scripts.
* Use **LF** line endings for Linux/Unix-oriented files, including shell scripts (`.sh`).
* Never mix CRLF and LF line endings within the same file.

## Braces & Blocks

* **Multi-line bodies:** Place both opening and closing curly braces on their own line.

  ```csharp
  if(foo)
  {
  	DoThis();
  	DoThat();
  }
  ```

* **Simple single-statement bodies:** Omit braces only when both the condition and the body are simple single-line constructs.

  ```csharp
  if(printName == true)
  	Print(name);
  ```

* **Nested statements:** Always use braces when the statement body itself contains another control structure.

  ```csharp
  if(isEnabled)
  {
  	for(int i = 0; i < 10; i++)
  		Process(i);
  }
  ```

* **Consistency across chains:** If any branch in an `if` / `else if` / `else` chain requires braces, then all branches in that chain must use braces.

  Preferred:

  ```csharp
  if(foo)
  {
  	DoSomething();
  }
  else
  {
  	DoThis();
  	DoThat();
  }
  ```

  Avoid:

  ```csharp
  if(foo)
  	DoSomething();
  else
  {
  	DoThis();
  	DoThat();
  }
  ```

## Multi-line Conditions & Expressions

* For multi-line parenthesized expressions, parameter lists, and argument lists, place the closing parenthesis on its own line.

  ```csharp
  if(
  	customer != null
  	&& customer.IsActive
  	&& customer.Balance > 0
  )
  {
  	ProcessCustomer(customer);
  }
  ```

  ```csharp
  public void StoreSomething(
  	string name,
  	int value
  )
  {
  	Store(name, value);
  }
  ```

* Reserve this formatting style for genuinely multi-line or structurally complex expressions.

* Keep short expressions on a single line when practical.

  ```csharp
  if(x > 0)
  	Process();
  ```

## Naming Conventions

* *Methods:* VerbNoun style (e.g., `StoreSomething`, `PlayTape`).
* *Classes, structs, enums, public members:* PascalCase.
* *Private fields and local variables:* camelCase.
* *Private readonly fields:* Prefix with an underscore (e.g., `_memoryTapeProvider`).

## Organization

### General Rules

* Use `#region` blocks grouped by **member type**.
* If a region is entirely **public**, omit accessibility in the region name (e.g., `#region properties`).
* If members are **non-public**, include accessibility in the region name (e.g., `#region private properties`).
* For fields, the default `#region fields` is **private**. Explicitly label `public fields`, `protected fields`, etc. only when needed.
* Maintain consistency across all files.
* Do not place blank lines immediately after a `#region` directive or immediately before the matching `#endregion`.

### Order of Member Types (top → bottom)

1. Events
2. Fields

   * `#region static fields` first, then `#region fields` (instance).
3. Constructors

   * `#region class constructors` (static) first, then `#region constructors` (instance).
4. Properties
5. Functions / Methods

   * `#region functions` for static methods.
   * `#region methods` for instance methods.
6. Operator Overloads

   * Dedicated `#region operator overloads` for operators and `implicit`/`explicit` casts.

### Order of Accessibility (within each member type/region)

1. public
2. internal
3. protected
4. private

### Inherited / Overridden Members

* Place overrides in a region named for the original declaring type and the member kind.

  * Examples:

    * `#region object methods` for `ToString`, `GetHashCode`, `Equals`.
    * `#region baseclass methods` if overriding from `BaseClass`.
    * `#region IDisposable methods` if implementing/overriding interface members explicitly.

## Testing Guidelines

* Never use randomness in unit tests.
* Always use deterministic values (fixed GUIDs, timestamps, constants).
* Prefer clarity over cleverness — avoid generators or implicit defaults.

## General Code Style

* Never use `var` — always declare the explicit type, even when obvious.
* Prefer explicitness and verbosity over brevity or “magic.”
* Always specify access modifiers (`public`, `private`, `internal`, etc.) — never rely on defaults.
* Avoid expression-bodied members (`=>`) unless absolutely necessary for clarity.
* Strive for one return per method unless multiple exits significantly improve clarity.
* Write code as if it will live in a long-lived production codebase: clean, consistent, maintainable.
* Use comments sparingly but effectively — explain *why*, not *what*.

## Defensive Programming & Nullability

* **Nullable Reference Types (NRTs):** Enable in new code (`<Nullable>enable</Nullable>`) and annotate reference types accurately (`string` vs `string?`).

* **Public/Protected entry points (API boundaries):**

  * Validate inputs and **throw** appropriate exceptions.

    * Use `ArgumentNullException.ThrowIfNull(argName);`
    * Use `string.IsNullOrWhiteSpace` checks for textual inputs.
    * Prefer specific exceptions (`ArgumentNullException`, `ArgumentException`, `ArgumentOutOfRangeException`).
  * **Rationale:** Callers may come from legacy or non-NRT code; runtime guarantees matter at boundaries.

    ```csharp
    public static Librarian CreateLibrarian(IPiedPiper? piedPiper, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(directoryPath));

        ArgumentNullException.ThrowIfNull(piedPiper);

        IPiedPiper actual = PreparePiedPiper(piedPiper);
        return new Librarian(new DiskTapeProvider(actual, directoryPath));
    }
    ```

* **Internal/Private code paths (owned invariants):**

  * Prefer **`Debug.Assert`** to enforce invariants that indicate *bugs* if violated.
  * Do **not** use asserts for control flow.
  * Assertions must be side-effect free and cheap.
  * Keep messages clear and actionable.

    ```csharp
    private static Librarian CreateLibrarian(IPiedPiper? piedPiper, string directoryPath)
    {
        System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(directoryPath), "directoryPath must be non-empty/non-whitespace.");
        System.Diagnostics.Debug.Assert(piedPiper is not null, "piedPiper must be provided for internal calls.");

        IPiedPiper actual = PreparePiedPiper(piedPiper);
        return new Librarian(new DiskTapeProvider(actual, directoryPath));
    }
    ```

* **Null-forgiving operator (`!`):**
  Use sparingly and only when you can *prove* non-null via prior guards or invariants; prefer a guard or `Debug.Assert` first.

* **Data contracts & serialization boundaries:**
  Treat deserialization, reflection, and DI activations as *external inputs* → validate and throw.

* **Documentation:**
  Method/parameter summaries should reflect nullability expectations (e.g., “`directoryPath` must be non-empty.”).
