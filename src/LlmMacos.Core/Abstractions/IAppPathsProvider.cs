using LlmMacos.Core.Models;

namespace LlmMacos.Core.Abstractions;

public interface IAppPathsProvider
{
    AppPaths Paths { get; }

    void EnsureCreated();
}
