using NBitcoin;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using WalletWasabi.Blockchain.TransactionBuilding.BnB;

namespace WalletWasabi.Blockchain.TransactionBuilding;

/// <seealso href="https://murch.one/wp-content/uploads/2016/11/erhardt2016coinselection.pdf">Section "5.3 Branch and Bound".</seealso>
public class BranchAndBound
{
	private readonly Random _random = new();

	/// <param name="values">All values must be strictly positive and in descending order.</param>
	public BranchAndBound(List<long> values)
	{
		if (values.Count == 0)
		{
			throw new ArgumentException("List is empty.");
		}

		if (values.Any(x => x <= 0))
		{
			throw new ArgumentException("Only strictly positive values are supported.");
		}

		Count = values.Count;
		SortedValues = values.OrderByDescending(x => x).ToArray();

		if (!values.SequenceEqual(SortedValues))
		{
			throw new ArgumentException("Input values must be sorted in descending order.");
		}
	}

	/// <remarks>Input values sorted in descending order.</remarks>
	private long[] SortedValues { get; }

	/// <summary>Number of input values.</summary>
	private int Count { get; }

	/// <summary>
	/// Attempts to find a set of values that sum up to the target value.
	/// </summary>
	/// <param name="searchStrategy">Search strategy that affects how the algorithm searches through the options.</param>
	/// <param name="selectedValues">Solution of the search algorithm based on <paramref name="searchStrategy"/> sorted in descending order.</param>
	/// <returns><c>true</c> when a match is found, <c>false</c> otherwise.</returns>
	public bool TryGetMatch(ISearchStrategy searchStrategy, [NotNullWhen(true)] out List<long>? selectedValues, CancellationToken cancellationToken = default)
	{
		selectedValues = null;

		if (SortedValues.Sum() < searchStrategy.Target)
		{
			return false;
		}

		if (TryFindSolution(searchStrategy, out long[]? solution, cancellationToken))
		{
			selectedValues = new List<long>();

			for (int i = 0; i < Count; i++)
			{
				if (solution[i] > 0)
				{
					selectedValues.Add(SortedValues[i]);
				}
			}

			return true;
		}

		return false;
	}

	private bool TryFindSolution(ISearchStrategy searchStrategy, [NotNullWhen(true)] out long[]? selection, CancellationToken cancellationToken = default)
	{
		// Current selection sum.
		long sum = 0L;

		// Current depth (think of the depth in the recursive algorithm sense).
		int depth = 0;

		selection = new long[Count];
		NextAction[] actions = new NextAction[Count];
		actions[0] = GetRandomNextAction();

		int i = 0;

		do
		{
			i++;
			NextAction action = actions[depth];

			// Branch WITH the value included.
			if (action == NextAction.IncludeFirstThenOmit || action == NextAction.Include)
			{
				actions[depth] = GetNextStep(action);
				sum = searchStrategy.UpdateSum(action, selection, depth, sum);

				EvaluationResult result = searchStrategy.Evaluate(selection, depth + 1, sum);

				if (result == EvaluationResult.SkipBranch)
				{
					continue;
				}
				else if (result == EvaluationResult.Match)
				{
					return true;
				}

				depth++;
				actions[depth] = GetRandomNextAction();
			}
			else if (action == NextAction.OmitFirstThenInclude || action == NextAction.Omit)
			{
				actions[depth] = GetNextStep(action);

				// Branch WITHOUT the value included.
				sum = searchStrategy.UpdateSum(action, selection, depth, sum);

				if (depth + 1 == Count)
				{
					// Leaf reached, no match.
					continue;
				}

				depth++;
				actions[depth] = GetRandomNextAction();
			}
			else
			{
				sum = searchStrategy.UpdateSum(action, selection, depth, sum);
				depth--;
			}

			// Micro optimization: Do not check cancellation token every time as it requires accessing volatile memory.
			if (i % 10_000 == 0 && cancellationToken.IsCancellationRequested)
			{
				return false;
			}
		}
		while (depth >= 0);

		return false;
	}

	private NextAction GetRandomNextAction()
	{
		return _random.Next(0, 2) == 1 ? NextAction.IncludeFirstThenOmit : NextAction.OmitFirstThenInclude;
	}

	private static NextAction GetNextStep(NextAction action)
	{
		return action switch
		{
			NextAction.IncludeFirstThenOmit => NextAction.Omit,
			NextAction.OmitFirstThenInclude => NextAction.Include,
			NextAction.Include => NextAction.Backtrack,
			NextAction.Omit => NextAction.Backtrack,
			NextAction.Backtrack => throw new InvalidOperationException("This should never happen."),
			_ => throw new InvalidOperationException("No other values are valid.")
		};
	}
}
