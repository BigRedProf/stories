# Coding Conventions

NOTE:
> This file is a snapshot of the BigRedProf coding standards.
> The canonical version lives in bigredprof/data.
> If you modify this file, update the canonical copy as well.

## Indentation & Spacing
- Always use **tabs** (never spaces) for indentation.
- Keep indentation consistent across all files.

## Braces & Blocks
- **Multi-line bodies:** Place both opening and closing curly braces on their own line.
- **Single-line bodies:** Do not use braces. Put the statement on the following line, indented one level.
  ```csharp
  if (printName == true)
      Print(name);
  ```
- Apply these rules consistently for all control structures (`if`, `else`, `for`, `while`, `foreach`, etc.).

## Naming Conventions
- *Methods:* VerbNoun style (e.g., `StoreSomething`, `PlayTape`).
- *Classes, structs, enums, public members:* PascalCase.
- *Private fields and local variables:* camelCase.
- *Private readonly fields:* Prefix with an underscore (e.g., `_memoryTapeProvider`).

## Organization

### General Rules
- Use `#region` blocks grouped by **member type**.
- If a region is entirely **public**, omit accessibility in the region name (e.g., `#region properties`).
- If members are **non-public**, include accessibility in the region name (e.g., `#region private properties`, `#region protected methods`).
- For fields, the default `#region fields` is **private**. Explicitly label `public fields`, `protected fields`, etc. only when needed.
- Maintain consistency across all files.

### Order of Member Types (top → bottom)
1. Events
2. Fields
   - `#region static fields` first, then `#region fields` (instance).
3. Constructors
   - `#region class constructors` (static) first, then `#region constructors` (instance).
4. Properties
5. Functions / Methods
   - `#region functions` for static methods.
   - `#region methods` for instance methods.
6. Operator Overloads
   - Dedicated `#region operator overloads` for operators and `implicit`/`explicit` casts.

### Order of Accessibility (within each member type/region)
1. public
2. internal
3. protected
4. private

### Inherited / Overridden Members
- Place overrides in a region named for the original declaring type and the member kind.
  - Examples:
    - `#region object methods` for `ToString`, `GetHashCode`, `Equals`.
    - `#region baseclass methods` if overriding from `BaseClass`.
    - `#region IDisposable methods` if implementing/overriding interface members explicitly.

## Testing Guidelines
- Never use randomness in unit tests.
- Always use deterministic values (fixed GUIDs, timestamps, constants).
- Prefer clarity over cleverness — avoid generators or implicit defaults.

## General Code Style
- Never use `var` — always declare the explicit type, even when obvious.
- Prefer explicitness and verbosity over brevity or “magic.”
- Always specify access modifiers (`public`, `private`, `internal`, etc.) — never rely on defaults.
- Avoid expression-bodied members (`=>`) unless absolutely necessary for clarity.
- Strive for one return per method unless multiple exits significantly improve clarity.
- Write code as if it will live in a long-lived production codebase: clean, consistent, maintainable.
- Use comments sparingly but effectively — explain *why*, not *what*.



---

using System;
using System.Globalization;

namespace BigRedProf.Math
{
	public sealed class Vector2 : IEquatable<Vector2>
	{
		#region events
		public event EventHandler? MagnitudeExceeded;
		#endregion

		#region public static fields
		public static readonly Vector2 Zero = new Vector2(0.0, 0.0);
		public static readonly Vector2 UnitX = new Vector2(1.0, 0.0);
		public static readonly Vector2 UnitY = new Vector2(0.0, 1.0);
		#endregion

		#region static fields
		private static readonly double Epsilon = 1e-12;
		#endregion

		#region fields
		private readonly double _x;
		private readonly double _y;
		#endregion

		#region class constructors
		static Vector2()
		{
		}
		#endregion

		#region constructors
		public Vector2(double x, double y)
		{
			this._x = x;
			this._y = y;
		}
		#endregion

		#region properties
		public double X
		{
			get { return this._x; }
		}

		public double Y
		{
			get { return this._y; }
		}

		public double Magnitude
		{
			get { return Math.Sqrt((this._x * this._x) + (this._y * this._y)); }
		}

		public double AngleRadians
		{
			get { return Math.Atan2(this._y, this._x); }
		}
		#endregion

		#region functions
		public static Vector2 Add(Vector2 left, Vector2 right)
		{
			return new Vector2(left._x + right._x, left._y + right._y);
		}

		public static Vector2 Subtract(Vector2 left, Vector2 right)
		{
			return new Vector2(left._x - right._x, left._y - right._y);
		}

		public static double Dot(Vector2 left, Vector2 right)
		{
			return (left._x * right._x) + (left._y * right._y);
		}

		public static Vector2 Scale(Vector2 value, double scalar)
		{
			return new Vector2(value._x * scalar, value._y * scalar);
		}

		public static Vector2 FromPolar(double magnitude, double angleRadians)
		{
			double cos = Math.Cos(angleRadians);
			double sin = Math.Sin(angleRadians);
			return new Vector2(magnitude * cos, magnitude * sin);
		}

		public static bool AreColinear(Vector2 a, Vector2 b)
		{
			double cross = (a._x * b._y) - (a._y * b._x);
			if (cross < 0.0)
				cross = -cross;
			return cross <= Epsilon;
		}
		#endregion

		#region methods
		public Vector2 Normalize()
		{
			double mag = this.Magnitude;
			if (mag <= Epsilon)
				return this;

			double inv = 1.0 / mag;
			return new Vector2(this._x * inv, this._y * inv);
		}

		public Vector2 WithX(double newX)
		{
			return new Vector2(newX, this._y);
		}

		public Vector2 WithY(double newY)
		{
			return new Vector2(this._x, newY);
		}

		public bool Equals(Vector2? other)
		{
			if (other is null)
				return false;

			return this._x == other._x && this._y == other._y;
		}

		#region object methods
		public override bool Equals(object? obj)
		{
			if (obj is Vector2 v)
				return this.Equals(v);
			return false;
		}

		public override int GetHashCode()
		{
			int hx = this._x.GetHashCode();
			int hy = this._y.GetHashCode();
			unchecked
			{
				int hash = 17;
				hash = (hash * 31) + hx;
				hash = (hash * 31) + hy;
				return hash;
			}
		}

		public override string ToString()
		{
			return string.Format(CultureInfo.InvariantCulture, "({0}, {1})", this._x, this._y);
		}
		#endregion
		#endregion

		#region operator overloads
		public static Vector2 operator +(Vector2 left, Vector2 right)
		{
			return new Vector2(left._x + right._x, left._y + right._y);
		}

		public static Vector2 operator -(Vector2 left, Vector2 right)
		{
			return new Vector2(left._x - right._x, left._y - right._y);
		}

		public static Vector2 operator *(Vector2 value, double scalar)
		{
			return new Vector2(value._x * scalar, value._y * scalar);
		}

		public static Vector2 operator *(double scalar, Vector2 value)
		{
			return new Vector2(value._x * scalar, value._y * scalar);
		}

		public static Vector2 operator /(Vector2 value, double scalar)
		{
			return new Vector2(value._x / scalar, value._y / scalar);
		}

		public static bool operator ==(Vector2 left, Vector2 right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(Vector2 left, Vector2 right)
		{
			return !left.Equals(right);
		}

		public static implicit operator double[](Vector2 value)
		{
			return new double[] { value._x, value._y };
		}

		public static explicit operator Vector2(double[] components)
		{
			if (components is null)
				throw new ArgumentNullException(nameof(components));

			if (components.Length != 2)
				throw new ArgumentException("Vector2 conversion requires an array of length 2.", nameof(components));

			return new Vector2(components[0], components[1]);
		}
		#endregion

		#region private methods
		private void RaiseMagnitudeExceededIfNeeded(double threshold)
		{
			if (this.Magnitude > threshold && this.MagnitudeExceeded is not null)
				this.MagnitudeExceeded(this, EventArgs.Empty);
		}
		#endregion
	}
}

## Defensive Programming & Nullability
- **Nullable Reference Types (NRTs):** Enable in new code (`<Nullable>enable</Nullable>`) and annotate reference types accurately (`string` vs `string?`).  

- **Public/Protected entry points (API boundaries):**  
  - Validate inputs and **throw** appropriate exceptions.  
    - Use `ArgumentNullException.ThrowIfNull(argName);`  
    - Use `string.IsNullOrWhiteSpace` checks for textual inputs.  
    - Prefer specific exceptions (`ArgumentNullException`, `ArgumentException`, `ArgumentOutOfRangeException`).  
  - **Rationale:** Callers may come from legacy or non-NRT code; runtime guarantees matter at boundaries.  

	  ```csharp
	  public static Librarian CreateLibrarian(IPiedPiper? piedPiper, string directoryPath)
	  {
	      if (string.IsNullOrWhiteSpace(directoryPath))
	          throw new ArgumentNullException(nameof(directoryPath));

	      ArgumentNullException.ThrowIfNull(piedPiper);

	      IPiedPiper actual = PreparePiedPiper(piedPiper);
	      return new Librarian(new DiskTapeProvider(actual, directoryPath));
	  }
	  ```

- **Internal/Private code paths (owned invariants):**  
  - Prefer **`Debug.Assert`** to enforce invariants that indicate *bugs* if violated.  
  - Do **not** use asserts for control flow.  
  - Assertions must be side-effect free and cheap.  
  - Keep messages clear and actionable.  

	  ```csharp
	  private static Librarian CreateLibrarian(IPiedPiper? piedPiper, string directoryPath)
	  {
	      System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(directoryPath), \"directoryPath must be non-empty/non-whitespace.\");
	      System.Diagnostics.Debug.Assert(piedPiper is not null, \"piedPiper must be provided for internal calls.\");

	      IPiedPiper actual = PreparePiedPiper(piedPiper);
	      return new Librarian(new DiskTapeProvider(actual, directoryPath));
	  }
	  ```

- **Null-forgiving operator (`!`):**  
  Use sparingly and only when you can *prove* non-null via prior guards or invariants; prefer a guard or `Debug.Assert` first.  

- **Data contracts & serialization boundaries:**  
  Treat deserialization, reflection, and DI activations as *external inputs* → validate and throw.  

- **Documentation:**  
  Method/parameter summaries should reflect nullability expectations (e.g., “`directoryPath` must be non-empty.”).  
