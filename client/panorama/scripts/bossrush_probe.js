// Boss Rush — Upgrade Station shop logic.
// The native shop is invisible (#Shop opacity 0) but still in-tree + data-bound, so we read the
// player's OWNED items out of its C++ lists and render our own tiles. Tabs drive which native
// category is active (so its list populates) and recolor the UI. Poll-based; the grid rebuilds
// only when the owned set changes. (Real icons + full stats bind in a follow-up pass.)
(function () {
    function topOf(p) { while (p && p.GetParent()) p = p.GetParent(); return p; }
    function first(panel, cls) { var a = panel.FindChildrenWithClassTraverse(cls); return (a && a.length) ? a[0] : null; }
    function setText(top, cls, txt) { var e = first(top, cls); if (e) e.text = txt; }

    function collect(panel, type, out, depth) {
        if (!panel || depth > 12) return;
        if (panel.paneltype === type) out.push(panel);
        var ch = null;
        try { ch = panel.Children(); } catch (e) {}
        if (ch) for (var i = 0; i < ch.length; i++) collect(ch[i], type, out, depth + 1);
    }

    // ── Tabs: category class (recolor) + native slot (populate) + placeholder copy ──
    var TAB_DEFS = [
        { cls: "BRTabWeapon",    cat: "catWeapon",    slot: "EItemSlotType_WeaponMod", list: "ShopModsListWeapon", name: "FAIRWAY",           grid: "OWNED · FAIRWAY",             action: "ENHANCE" },
        { cls: "BRTabArmor",     cat: "catArmor",     slot: "EItemSlotType_Armor",     list: "ShopModsListArmor",  name: "MPS",               grid: "OWNED · MPS",                 action: "ENHANCE" },
        { cls: "BRTabTech",      cat: "catTech",      slot: "EItemSlotType_Tech",      list: "ShopModsListTech",   name: "CURIOSITY CATALOG", grid: "OWNED · CURIOSITY CATALOG",   action: "ENHANCE" },
        { cls: "BRTabLegendary", cat: "catLegendary", slot: "EItemSlotType_All",       list: "ShopModsListAll",    name: "MYTHIC",            grid: "MYTHIC ALTAR · FOR PURCHASE", action: "BUY RELIC" }
    ];
    var activeDef = TAB_DEFS[0];
    var lastGridSig = "";

    function selectTab(top, modal, def) {
        activeDef = def;
        TAB_DEFS.forEach(function (d) {
            var t = first(top, d.cls);
            if (t) t.SetHasClass("BRTabActive", d === def);
            modal.SetHasClass(d.cat, d === def);
        });
        setText(top, "BRDetailCat", def.name);
        setText(top, "BRGridTitle", def.grid);
        setText(top, "BRActionLbl", def.action);
        lastGridSig = "";                                  // force a rebuild for the new category
        $.DispatchEvent("CitadelShopModsActivate", def.slot);  // make the native list populate
        // Snappy switch: refresh as soon as the native list repopulates, don't wait for the next poll.
        $.Schedule(0.03, function () { populateGrid(top); });
        $.Schedule(0.12, function () { populateGrid(top); });
    }

    var tabsWired = false;
    function wireTabs(top, modal) {
        if (tabsWired) return;
        for (var i = 0; i < TAB_DEFS.length; i++) { if (!first(top, TAB_DEFS[i].cls)) return; }
        TAB_DEFS.forEach(function (def) {
            first(top, def.cls).SetPanelEvent("onactivate", function () { selectTab(top, modal, def); });
        });
        tabsWired = true;
        selectTab(top, modal, activeDef);                  // initialise native + texts
    }

    // ── Read owned items out of the (invisible) native list ──
    function readOwned(top, listId) {
        var L = top.FindChildTraverse(listId);
        var out = [];
        if (!L) return out;
        var cards = [];
        collect(L, "CitadelShopMod", cards, 0);
        for (var i = 0; i < cards.length; i++) {
            var c = cards[i];
            var owned = false;
            try { owned = c.BHasClass("owned") || c.BHasClass("usedAsComponent"); } catch (e) {}
            if (!owned) continue;
            var nmEl = first(c, "modName");
            out.push({ name: nmEl ? nmEl.text : "" });
        }
        return out;
    }

    function selectDetail(top, grid, item, tile) {
        if (grid) {
            var tiles = grid.FindChildrenWithClassTraverse("BRTile");
            for (var i = 0; i < tiles.length; i++) tiles[i].SetHasClass("BRTileSelected", tiles[i] === tile);
        }
        setText(top, "BRDetailName", (item.name || "").toUpperCase());
        setText(top, "BRDetailTier", "");
        setText(top, "BRDetailPrice", "");
        setText(top, "BRDetailDesc", "Icon + stats bind in the next pass.");
    }

    function populateGrid(top) {
        var items = readOwned(top, activeDef.list);
        var sig = activeDef.list + ":" + items.length + ":" + items.map(function (i) { return i.name; }).join("|");
        if (sig === lastGridSig) return;
        lastGridSig = sig;

        var grid = first(top, "BRGrid");
        if (!grid) return;
        grid.RemoveAndDeleteChildren();

        var row = null;
        items.forEach(function (it, idx) {
            if (idx % 4 === 0) { row = $.CreatePanel("Panel", grid, ""); row.AddClass("BRRow"); }
            var tile = $.CreatePanel("Panel", row, "");
            tile.AddClass("BRTile");
            $.CreatePanel("Panel", tile, "").AddClass("BRTileIcon");
            var bar = $.CreatePanel("Panel", tile, ""); bar.AddClass("BRTileNameBar");
            var nm = $.CreatePanel("Label", bar, ""); nm.AddClass("BRTileName"); nm.text = it.name;
            (function (item, t) { t.SetPanelEvent("onactivate", function () { selectDetail(top, grid, item, t); }); })(it, tile);
        });

        setText(top, "BRHeld", items.length + " HELD");

        if (items.length > 0) {
            var fr = grid.GetChild(0);
            var ft = fr ? fr.GetChild(0) : null;
            if (ft) selectDetail(top, grid, items[0], ft);
        }
    }

    function apply() {
        var top = topOf($.GetContextPanel());
        var modal = top ? first(top, "BRModal") : null;
        if (!top || !modal) { $.Schedule(0.5, apply); return; }
        wireTabs(top, modal);
        // Self-heal: on shop open the active native list sometimes isn't populated until re-activated
        // (a tab round-trip would otherwise be needed). If it has no cards yet, re-dispatch its activate.
        var L = top.FindChildTraverse(activeDef.list);
        if (L) { var nc = []; collect(L, "CitadelShopMod", nc, 0); if (nc.length === 0) $.DispatchEvent("CitadelShopModsActivate", activeDef.slot); }
        populateGrid(top);
        $.Schedule(0.3, apply);
    }

    apply();
})();
