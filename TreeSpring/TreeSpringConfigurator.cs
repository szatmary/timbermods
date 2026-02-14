using Bindito.Core;
using Timberborn.TemplateInstantiation;

namespace TreeSpring;

[Context("Game")]
public class TreeSpringConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<TreeSpringBattery>().AsTransient();
        Bind<TreeSpringAnimator>().AsTransient();
        MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule).AsSingleton();
    }

    private static TemplateModule ProvideTemplateModule()
    {
        TemplateModule.Builder builder = new TemplateModule.Builder();
        builder.AddDecorator<TreeSpringBatterySpec, TreeSpringBattery>();
        builder.AddDecorator<TreeSpringBatterySpec, TreeSpringAnimator>();
        return builder.Build();
    }
}
