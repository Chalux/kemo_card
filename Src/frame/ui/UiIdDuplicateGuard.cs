using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Ui;

public sealed class UiIdDuplicateGuard
{
	private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

	public void Add(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		if (!_ids.Add(id))
		{
			throw new InvalidOperationException($"UI id already registered: {id}");
		}
	}
}
