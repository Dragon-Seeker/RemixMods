using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Menu.Remix.MixedUI;
using Menu.Remix.MixedUI.ValueTypes;
using UnityEngine;

namespace SplitScreenCoop;

public class SplitScreenCoopOptions : OptionInterface
{
    class BetterComboBox : OpComboBox
    {
        public BetterComboBox(ConfigurableBase configBase, Vector2 pos, float width, List<ListItem> list) : base(configBase, pos, width, list) { }
        public override void GrafUpdate(float timeStacker)
        {
            base.GrafUpdate(timeStacker);
            if(this._rectList != null && !_rectList.isHidden)
            {
                for (int j = 0; j < 9; j++)
                {
                    this._rectList.sprites[j].alpha = 1;
                }
            }
        }
    }
    public SplitScreenCoopOptions()
    {
        PreferredSplitMode = this.config.Bind("PreferredSplitMode", SplitScreenCoop.SplitMode.SplitVertical);
        AlwaysSplit = this.config.Bind("AlwaysSplit", false);
        AllowCameraSwapping = this.config.Bind("AllowCameraSwapping", false);

        //Code below is to prevent issues when min and max value for the range is the same or zero so we disable the option if not valid down below
        var num = SplitScreenCoop.AllowedAdditionalDisplayCount();
        
        AdditionalDisplayCount = this.config.Bind("AdditionalDisplays", 0, new ConfigAcceptableRange<int>(0, (num != 0 ? num : 3)));

        //Prevent people who can not use 
        if (AdditionalDisplayCount.Value > num)
        {
            AdditionalDisplayCount.Value = num;
            
            this.config.Save();
        }
        
        AddRealizerPerPlayer = this.config.Bind("AddRealizerPerPlayer", false);
        TestSomething = this.config.Bind("TestSomething", false);
    }

    public readonly Configurable<SplitScreenCoop.SplitMode> PreferredSplitMode;
    public readonly Configurable<bool> AlwaysSplit;
    public readonly Configurable<bool> AllowCameraSwapping;
    
    public readonly Configurable<int> AdditionalDisplayCount;
    public readonly Configurable<bool> AddRealizerPerPlayer;
    
    public readonly Configurable<bool> TestSomething;
    
    private UIelement[] UIArrOptions;

    public override void Initialize()
    {
        var opTab = new OpTab(this, "Options");
        this.Tabs = new[] { opTab };
        OpSlider slider;
        UIArrOptions = new UIelement[]
        {
            new OpLabel(10f, 550f, "General", true),

            new OpCheckBox(AlwaysSplit, 10f, 450),
            new OpLabel(40f, 450, "Permanent split mode") { verticalAlignment = OpLabel.LabelVAlignment.Center },

            new OpCheckBox(AllowCameraSwapping, 10f, 410),
            new OpLabel(40f, 410, "Allow camera swapping even if there's enough cameras") { verticalAlignment = OpLabel.LabelVAlignment.Center },

            new OpLabel(10f, 370, "Experimental", true),
            
            slider = new OpSlider(AdditionalDisplayCount, new Vector2(21f, 330), 80),
            new OpLabel(118, 335, "Number of Additional Displays to use") { verticalAlignment = OpLabel.LabelVAlignment.Center },
            
            new OpCheckBox(AddRealizerPerPlayer, 10f, 310),
            new OpLabel(40f, 310, "Add Realizer Per Player playing") { verticalAlignment = OpLabel.LabelVAlignment.Center },
                
            new OpCheckBox(TestSomething, 10f, 285),
            new OpLabel(40f, 285, "Test some coop changes") { verticalAlignment = OpLabel.LabelVAlignment.Center },
            
            // added last due to overlap
            new OpLabel(10f, 520, "Split Mode") { verticalAlignment = OpLabel.LabelVAlignment.Center },
            new BetterComboBox(PreferredSplitMode, new Vector2(10f, 490), 200f, OpResourceSelector.GetEnumNames(null, typeof(SplitScreenCoop.SplitMode)).ToList()),
        };

        slider.greyedOut = SplitScreenCoop.AllowedAdditionalDisplayCount() == 0;
        
        // Add items to the tab
        opTab.AddItems(UIArrOptions);
    }
}