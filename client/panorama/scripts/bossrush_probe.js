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

    // Sell → ENHANCE on the hover-tooltip card preview. The native rule
    // `#CitadelModHoverTooltip.owned #SellOverlay { visibility: visible }` shows "Sell" on the
    // tooltip's #ModView clone; our vcss override didn't win, so we flip it in JS instead —
    // inline visibility beats the stylesheet and is deterministic. Only the tooltip preview is
    // touched (the real grid card flip is handled by .BRTile CSS). Cheap: one traverse, gated to
    // when the shop is open.
    function flipHoverTooltip(top) {
        var tt = top.FindChildTraverse("CitadelModHoverTooltip");
        if (!tt) return;
        var owned = false; try { owned = tt.BHasClass("owned"); } catch (e) {}
        if (!owned) return;
        var so = null, eo = null;
        try { so = tt.FindChildTraverse("SellOverlay"); } catch (e) {}
        try { eo = tt.FindChildTraverse("EnhanceOverlay"); } catch (e) {}
        try { if (so) so.visible = false; } catch (e) {}
        try { if (eo) eo.visible = true; } catch (e) {}
        if (!eo) return;

        // Native #EnhanceOverlay shows "Enhance" + name only — inject the 2× cost, styled to match the
        // native sell price (souls icon + ~16px bold number, centered). The tooltip is a fresh panel each
        // hover, so our row vanishes with it; re-add when missing (id guards dupes).
        var has = null; try { has = eo.FindChildTraverse("BREnhPrice"); } catch (e) {}
        if (has) return;
        var base = 0, ic = null;
        try { ic = tt.FindChildTraverse("ItemCost"); } catch (e) {}
        if (ic) { try { base = parseCost(ic.text); } catch (e) {} }
        var price = base ? base * 2 : 0;
        if (!price) return;   // no cost read → skip the row rather than show a bare word
        var mc = eo;
        try { var arr = eo.FindChildrenWithClassTraverse("MessageContent"); if (arr && arr.length) mc = arr[0]; } catch (e) {}
        var row = $.CreatePanel("Panel", mc, "BREnhPrice");
        try { row.style.flowChildren = "right"; row.style.horizontalAlign = "center"; row.style.marginTop = "6px"; } catch (e) {}
        // reuse the real native souls icon from the (hidden) sell overlay for an exact match
        if (so) {
            var gi = null; try { gi = so.FindChildrenWithClassTraverse("goldIcon"); } catch (e) {}
            if (gi && gi.length) {
                try { gi[0].SetParent(row); gi[0].style.width = "18px"; gi[0].style.height = "18px"; gi[0].style.marginRight = "5px"; gi[0].style.verticalAlign = "middle"; gi[0].visible = true; } catch (e) {}
            }
        }
        var amt = $.CreatePanel("Label", row, "");
        amt.text = fmt(price);
        try {
            amt.style.fontSize = "17px"; amt.style.fontWeight = "bold"; amt.style.color = "#ffffff";
            amt.style.verticalAlign = "middle"; amt.style.textShadow = "0px 0px 5px 2.0 #000000";
        } catch (e) {}
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

    var gridTick = 0, lastGridDbg = "";
    function populateGrid(top) {
        gridTick++;
        // Rebuild on tab change, otherwise periodically (~2.4s) so enhancing/selling refreshes the cards
        // instead of lingering until a tab switch. (Can't cheaply detect the change without rebuilding,
        // since borrowed cards leave the native list — so we throttle a periodic rebuild.)
        if (activeDef.list === lastGridSig && (gridTick % 8 !== 0)) return;
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

            // Hide the native "OWNED" overlay (#ItemPurchased is the owned/sold-out badge in the card).
            try { var ip = it.card.FindChildTraverse("ItemPurchased"); if (ip) ip.visible = false; } catch (e) {}
            // NOTE: no client-side "enhanced" detection — `.isEnhanced` is unreliable here because the
            // citadel_shop_items_appear_enhanced convar (if set) makes EVERY card report enhanced. Every owned
            // item stays clickable; re-enhancing an already-enhanced item is guarded server-side instead.

            var bar = $.CreatePanel("Panel", tile, ""); bar.AddClass("BRTileNameBar");
            try { bar.hittest = false; } catch (e) {}   // don't steal hover from the card
            var nm = $.CreatePanel("Label", bar, ""); nm.AddClass("BRTileName"); nm.text = it.name;
        });

        setText(top, "BRHeld", items.length + " HELD");
    }

    // The native "Sell Item?" confirm (a popup_generic instance: #TitleLabel + #MessageLabel + OK/Cancel) only
    // pops for USED items. We relabel it in place to "Enhance Item?" + the 2× price — its OK natively sends the
    // "sellitem <name>" command, which the server intercepts and turns into an enhance, so we leave the button
    // alone. Key off the live title text (C++ resets it to "Sell Item?" for each new sell), so this re-runs per
    // appearance and never double-processes (after relabel the title no longer says "sell item").
    // Find the first Label under `panel` whose (html-stripped) text contains `needle` (lowercase).
    function findLabelByText(panel, needle, depth) {
        if (!panel || depth > 18) return null;
        var t = null; try { t = panel.text; } catch (e) {}
        if (t && ("" + t).replace(/<[^>]*>/g, "").toLowerCase().indexOf(needle) >= 0) return panel;
        var ch = null; try { ch = panel.Children(); } catch (e) {}
        if (ch) for (var i = 0; i < ch.length; i++) { var r = findLabelByText(ch[i], needle, depth + 1); if (r) return r; }
        return null;
    }

    function handleSellPopup(top) {
        var title = top.FindChildTraverse("TitleLabel");
        if (!title) return;
        var ttxt = ""; try { ttxt = "" + (title.text || ""); } catch (e) { return; }
        if (ttxt.toLowerCase().indexOf("sell item") < 0) return;

        // Relabel the title FIRST so it always flips (and the "sell item" guard above stops re-processing).
        try { title.text = "Enhance Item?"; } catch (e) {}

        // The message label's id ("MessageLabel") isn't reliable here, so find it by content.
        var msg = findLabelByText(top, "purchase price", 0) || findLabelByText(top, "want to sell", 0);
        if (!msg) return;
        var mtxt = ""; try { mtxt = ("" + (msg.text || "")).replace(/<[^>]*>/g, ""); } catch (e) {}   // strip html=true markup
        var m = /sell item (.+?) for ([\d,]+)/i.exec(mtxt);
        var newText;
        if (m) {
            var disp = m[1].trim();
            var sell = parseInt(m[2].replace(/[^0-9]/g, ""), 10) || 0;  // sell = 0.5 × base → enhance = 2 × base = 4 × sell
            newText = "Enhance " + disp + " for " + fmt(sell * 4) + " souls (2× the purchase price)?";
        } else {
            newText = "Enhance this item for 2× its purchase price?";
        }
        try { msg.text = newText; } catch (e) {}
    }

    var cTop = null, cModal = null, cStation = null;
    function apply() {
        if (!cTop) cTop = topOf($.GetContextPanel());
        if (!cTop) { $.Schedule(0.5, apply); return; }
        if (!cModal)   cModal   = first(cTop, "BRModal");
        if (!cStation) cStation = first(cTop, "BRStation");
        if (!cModal || !cStation) { $.Schedule(0.5, apply); return; }

        // Only do heavy work while the shop is actually on screen. This poll runs forever; when the
        // shop is closed it must stay cheap, or it stalls the main thread every tick — which also
        // drives usercmd send, so a stall there desyncs client prediction and hitches the game.
        // BRStation collapses to 0 width when the shop is hidden → single-property open check.
        var open = false;
        try { open = cStation.actuallayoutwidth > 0; } catch (e) {}
        if (!open) { $.Schedule(0.4, apply); return; }

        wireTabs(cTop, cModal);
        var L = cTop.FindChildTraverse(activeDef.list);
        if (L) { var nc = []; collect(L, "CitadelShopMod", nc, 0); if (nc.length === 0) $.DispatchEvent("CitadelShopModsActivate", activeDef.slot); }
        populateGrid(cTop);
        flipHoverTooltip(cTop);
        handleSellPopup(cTop);
        $.Schedule(0.3, apply);
    }

    apply();
})();
