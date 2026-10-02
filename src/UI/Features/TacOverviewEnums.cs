namespace WvWarlord.UI.Features
{
    /// <summary>View Options: Single Map (vertical list), All Map Grid (4 type-columns, Card), Detail Grid (4 type-columns, CardPro), Map (macro coordinates -- not yet implemented).</summary>
    public enum TacOverviewViewMode
    {
        SingleMap,
        AllMapGrid,
        DetailGrid,
        Map
    }

    /// <summary>Filters, relative to the player's own team color.</summary>
    public enum TacOverviewFilterMode
    {
        All,
        MyColor,
        NotMyColor
    }

    public enum TacOverviewSortMode
    {
        BuildingType,
        PlayerDistance
    }
}
