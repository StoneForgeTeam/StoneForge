// Draw GUI End: the mods' DrawGui handlers (o_stonemod_gui - persistent, made by StoneForge once a mod
// draws, deep in front of everything).
scr_stonemod_draw_gui()
// The game draws its cursor in o_cursorController's Draw GUI, which comes before this, so mod UI would cover
// it. Run that event again on top: same cursor, target cursor and feather (nothing when the cursor is native).
with (o_cursorController)
    event_perform(ev_draw, ev_gui)
