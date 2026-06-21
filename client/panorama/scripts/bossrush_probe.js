// Boss Rush — Upgrade Station shop logic.
// Native shop is invisible (#Shop opacity 0) but live + data-bound; we read owned items out of its
// C++ lists and render our own tiles. Tabs drive the native category (so its list populates) + recolor.
// Icons: the game's icon files are snake_case(display name), e.g. "High-Velocity Rounds" ->
// items/weapon/high_velocity_rounds_psd.vtex. We derive the path from the item's display name (the
// card class uses stale internal names, so it can't be trusted). A small override map fixes renames.
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

    // ── Tabs ──
    var TAB_DEFS = [
        { cls: "BRTabWeapon",    cat: "catWeapon",    slot: "EItemSlotType_WeaponMod", list: "ShopModsListWeapon", dir: "weapon",   name: "FAIRWAY",           grid: "OWNED · FAIRWAY",             action: "ENHANCE" },
        { cls: "BRTabArmor",     cat: "catArmor",     slot: "EItemSlotType_Armor",     list: "ShopModsListArmor",  dir: "vitality", name: "MPS",               grid: "OWNED · MPS",                 action: "ENHANCE" },
        { cls: "BRTabTech",      cat: "catTech",      slot: "EItemSlotType_Tech",      list: "ShopModsListTech",   dir: "spirit",   name: "CURIOSITY CATALOG", grid: "OWNED · CURIOSITY CATALOG",   action: "ENHANCE" },
        { cls: "BRTabLegendary", cat: "catLegendary", slot: "EItemSlotType_All",       list: "ShopModsListAll",    dir: "",         name: "MYTHIC",            grid: "MYTHIC ALTAR · FOR PURCHASE",  action: "BUY RELIC" }
    ];
    var activeDef = TAB_DEFS[0];
    var lastGridSig = "";
    var currentCards = [];   // cards currently shown; relabeled each poll so the ENHANCE text sticks

    // Display name -> icon filename, only for items whose displayed name differs from the icon file.
    var ICON_OVERRIDES = {
        "extended magazine": "basic_magazine",
        "recharging rush": "recharging_rounds",
        "sharpshooter": "sharp_shooter",
        "spellslinger": "spell_slinger",
        "golden goose egg": "goose_egg",
        "mystic expansion": "greater_expansion",
        "mystic regeneration": "mystic_regen",
        "cursed relic": "curse",
        "compress cooldown": "improved_cooldown",
        "silence wave": "silence_glyph"
        // No icon file in items/ yet: Stalker, Ballistic Enchantment, Spirit Rend, Dispel Magic.
    };

    function iconFor(def, name) {
        if (!def.dir || !name) return null;
        var key = name.toLowerCase();
        var file = ICON_OVERRIDES[key] || key.replace(/['’]/g, "").replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "");
        if (!file) return null;
        return "s2r://panorama/images/items/" + def.dir + "/" + file + "_psd.vtex";
    }

    function selectTab(top, modal, def) {
        activeDef = def;
        TAB_DEFS.forEach(function (d) {
            var t = first(top, d.cls);
            if (t) t.SetHasClass("BRTabActive", d === def);
            modal.SetHasClass(d.cat, d === def);
        });
        setText(top, "BRGridTitle", def.grid);
        lastGridSig = "";
        $.DispatchEvent("CitadelShopModsActivate", def.slot);
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
        selectTab(top, modal, activeDef);
    }

    // ── Data ──
    var TIER_OF = { 800: 1, 1600: 2, 3200: 3, 6400: 4 };
    function parseCost(s) { var n = parseInt((s || "").replace(/[^0-9]/g, ""), 10); return isNaN(n) ? 0 : n; }
    function fmt(n) { return (n + "").replace(/\B(?=(\d{3})+(?!\d))/g, ","); } // commas; toLocaleString isn't in Panorama JS

    function readOwned(top, def) {
        var L = top.FindChildTraverse(def.list);
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
            var name = nmEl ? nmEl.text : "";
            var costEl = c.FindChildTraverse("ItemCost");
            var cost = costEl ? parseCost(costEl.text) : 0;
            out.push({ name: name, cost: cost, tier: TIER_OF[cost] || 0, icon: iconFor(def, name), card: c });
        }
        return out;
    }

    function setIcon(panel, path) {
        if (!panel) return;
        try { panel.style.backgroundImage = path ? ('url("' + path + '")') : 'none'; } catch (e) {}
        try { panel.style.backgroundSize = '100% 100%'; } catch (e) {}
    }

    // We move (SetParent) each card's real icon Image into our tile for a live, exact icon. Because our
    // grid rebuild deletes children, every borrowed icon must be returned to its native card first.
    var borrowedIcons = [];
    function returnBorrowed() {
        for (var i = 0; i < borrowedIcons.length; i++) {
            var b = borrowedIcons[i];
            try { if (b.parent) b.img.SetParent(b.parent); } catch (e) {}
        }
        borrowedIcons = [];
    }
    function borrowIcon(card, slot) {
        var mii = card ? card.FindChildTraverse("ModIconImage") : null;
        if (!mii) return;
        var op = null;
        try { op = mii.GetParent(); } catch (e) {}
        try {
            mii.SetParent(slot);
            mii.style.width = "100%"; mii.style.height = "100%";
            borrowedIcons.push({ img: mii, parent: op });
        } catch (e) {}
    }

    // EXPERIMENT: reparent the whole native card into the tile so hovering the tile hovers the card,
    // which should fire the C++ hover tooltip (CitadelModHoverTooltip). Visually rough — diagnostic.
    function borrowCard(card, tile) {
        if (!card) return;
        var op = null;
        try { op = card.GetParent(); } catch (e) {}
        try {
            card.SetParent(tile);
            try { card.hittest = true; } catch (e) {}
            card.style.width = "100%"; card.style.height = "100%";
            borrowedIcons.push({ img: card, parent: op });
        } catch (e) {}
    }

    // Our own ENHANCE badge, layered on top of the native sell badge (hittest off so the card still
    // gets the hover for its tooltip). Shown via .BRTile:hover in CSS. Covers the native "Sell".
    function addEnhanceBadge(tile, item) {
        var legendary = (activeDef.cat === "catLegendary");
        var word = legendary ? "BUY RELIC" : "ENHANCE";
        var price = legendary ? 25000 : (item.cost ? item.cost * 2 : 0);
        var ov = $.CreatePanel("Panel", tile, ""); ov.AddClass("BREnhOverlay");
        try { ov.hittest = false; } catch (e) {}
        var w = $.CreatePanel("Label", ov, ""); w.AddClass("BREnhWord"); w.text = word;
        var nm = $.CreatePanel("Label", ov, ""); nm.AddClass("BREnhName"); nm.text = item.name;
        var pr = $.CreatePanel("Label", ov, ""); pr.AddClass("BREnhPrice"); pr.text = "◈ " + fmt(price);
    }

    function selectDetail(top, grid, item, tile) {
        if (grid) {
            var tiles = grid.FindChildrenWithClassTraverse("BRTile");
            for (var i = 0; i < tiles.length; i++) tiles[i].SetHasClass("BRTileSelected", tiles[i] === tile);
        }
        setText(top, "BRDetailName", (item.name || "").toUpperCase());
        setText(top, "BRDetailCat", activeDef.name);
        setText(top, "BRDetailTier", item.tier ? ("TIER " + item.tier) : "");
        setText(top, "BRDetailPrice", item.cost ? ("◈ " + fmt(item.cost)) : "");
        setText(top, "BRDetailDesc", ""); // description/stats come from the tooltip — pending a decision on how
        setIcon(first(top, "BRDetailIcon"), item.icon);
        if (activeDef.cat === "catLegendary") setText(top, "BRActionLbl", "BUY RELIC");
        else setText(top, "BRActionLbl", item.cost ? ("ENHANCE  ◈ " + fmt(item.cost * 2)) : "ENHANCE");
    }

    function populateGrid(top) {
        if (activeDef.list === lastGridSig) return;   // category unchanged → keep tiles (cards stay hoverable)
        lastGridSig = activeDef.list;

        returnBorrowed();                              // cards back to the native list before reading it
        var items = readOwned(top, activeDef);
        var grid = first(top, "BRGrid");
        if (!grid) return;
        grid.RemoveAndDeleteChildren();
        currentCards = [];

        var row = null;
        items.forEach(function (it, idx) {
            if (idx % 6 === 0) { row = $.CreatePanel("Panel", grid, ""); row.AddClass("BRRow"); }
            var tile = $.CreatePanel("Panel", row, "");
            tile.AddClass("BRTile");
            borrowCard(it.card, tile);   // reparent the real native card in (icon + native hover tooltip)
            var bar = $.CreatePanel("Panel", tile, ""); bar.AddClass("BRTileNameBar");
            try { bar.hittest = false; } catch (e) {}   // don't steal hover from the card
            var nm = $.CreatePanel("Label", bar, ""); nm.AddClass("BRTileName"); nm.text = it.name;
            addEnhanceBadge(tile, it);   // our ENHANCE badge, layered over the native sell badge
        });

        setText(top, "BRHeld", items.length + " HELD");
    }

    function apply() {
        var top = topOf($.GetContextPanel());
        var modal = top ? first(top, "BRModal") : null;
        if (!top || !modal) { $.Schedule(0.5, apply); return; }
        wireTabs(top, modal);
        var L = top.FindChildTraverse(activeDef.list);
        if (L) { var nc = []; collect(L, "CitadelShopMod", nc, 0); if (nc.length === 0) $.DispatchEvent("CitadelShopModsActivate", activeDef.slot); }
        populateGrid(top);
        $.Schedule(0.3, apply);
    }

    apply();
})();
