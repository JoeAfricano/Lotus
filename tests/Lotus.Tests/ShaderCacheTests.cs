using Lotus.Core;
using Lotus.Core.Tools;
using Xunit;

namespace Lotus.Tests;

public sealed class ShaderCacheTests
{
    [Fact] public void DescribesEachDriverValue()
    {
        var text = new TextCatalog(); var c = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        Assert.Equal(text["ScUnlimited"], ShaderCacheRules.Describe(new(ShaderCacheRules.Unlimited, false), text, c));
        Assert.Equal(text["ScDefault"], ShaderCacheRules.Describe(new(10240, true), text, c));
        Assert.Equal(text["ScOff"], ShaderCacheRules.Describe(new(0, false), text, c));
        Assert.Equal(text.Format("ScSmall", "1 GB"), ShaderCacheRules.Describe(new(1024, false), text, c));
        Assert.Equal(text.Format("ScSize", "10 GB"), ShaderCacheRules.Describe(new(10240, false), text, c));
        Assert.Equal(text.Format("ScSmall", "512 MB"), ShaderCacheRules.Describe(new(512, false), text, c));
        Assert.True(ShaderCacheRules.Small(new(4096, false)));
        Assert.False(ShaderCacheRules.Small(new(4096, true)));
        Assert.False(ShaderCacheRules.Small(new(ShaderCacheRules.Unlimited, false)));
    }
}
