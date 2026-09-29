"""Read-only sprite playback using the same X/Y registration as the UO encoder."""
from pathlib import Path
import json
import sys
import tkinter as tk
from tkinter import ttk, messagebox
from PIL import Image, ImageTk

ROOT = Path(__file__).resolve().parent
SOURCES = {
    'Idle-first FIDGETS': ('fidget-idle-first/frames/frames.json', 'fidget-idle-first/frames'),
    'Idle-first RIGHT then LEFT': ('right-first-walk/frames/frames.json', 'right-first-walk/frames'),
    'UO stride matching trial — BLACK FUR': ('idle-walk-rebuild/stride-blackfur-draft.json', 'idle-walk-rebuild/stride-blackfur-frames'),
    'Alternating step trial (front only)': ('idle-walk-rebuild/fluid-draft.json', 'idle-walk-rebuild/fluid-frames'),
    'Human male (body 400)': ('human-400/frames.json', 'human-400'),
    'Human female (body 401)': ('human-401/frames.json', 'human-401'),
    'New walk draft (front only)': ('idle-walk-rebuild/front-draft.json', 'idle-walk-rebuild/frames'),
    'Current installed revision': ('walk-idle-match/frames/frames.json', 'walk-idle-match/packed/decoded'),
    'Slim revision': ('redesign-slim/frames/frames.json', 'redesign-slim/packed/decoded'),
    'Original UO': ('original/frames.json', 'original'),
}
SOURCES = {k: v for k, v in SOURCES.items() if (ROOT / v[0]).exists()}


def load_source(name):
    manifest, folder = SOURCES[name]
    records = json.loads((ROOT / manifest).read_text())
    groups = {}
    for record in records:
        image = Image.open(ROOT / folder / record['File']).convert('RGBA')
        if image.size != (record['Width'], record['Height']):
            raise ValueError(f"Dimensions mismatch: {record['File']}")
        groups.setdefault((record['Action'], record['Direction']), []).append((record, image))
    for frames in groups.values():
        frames.sort(key=lambda pair: pair[0]['Frame'])
    # Viewer-facing order: front -> left -> back -> right -> front.
    # Mirror the complete image AND its pixel-index anchor about the same root.
    for (action, direction), frames in list(groups.items()):
        if direction not in (1, 2, 3):
            continue
        mirrored = []
        for record, sprite in frames:
            m = dict(record, Direction=8-direction, X=sprite.width-1-record['X'], MirroredFrom=direction)
            if 'PelvisX' in m:
                m['PelvisX'] = sprite.width-1-record['PelvisX']
                assert m['PelvisX']-m['X'] == -(record['PelvisX']-record['X'])
            mirrored.append((m, sprite.transpose(Image.Transpose.FLIP_LEFT_RIGHT)))
        groups.setdefault((action, 8-direction), mirrored)
    return groups


class Panel:
    def __init__(self, app, parent, title, source, action):
        self.app = app
        self.box = ttk.LabelFrame(parent, text=title, padding=10)
        self.box.pack(side='left', fill='both', expand=True, padx=5)
        self.source = tk.StringVar(value=source)
        self.combo = ttk.Combobox(self.box, textvariable=self.source, values=list(SOURCES), state='readonly', width=35)
        self.combo.pack(fill='x')
        self.combo.bind('<<ComboboxSelected>>', lambda _: self.reload())
        self.action = tk.StringVar(value=str(action))
        self.actions = ttk.Combobox(self.box, textvariable=self.action, state='readonly')
        self.actions.pack(fill='x', pady=6)
        self.actions.bind('<<ComboboxSelected>>', lambda _: app.reset())
        self.canvas = tk.Canvas(self.box, background='#272b29', highlightthickness=0)
        self.canvas.pack(fill='both', expand=True)
        self.canvas.bind('<Configure>', lambda _: app.draw())
        self.info = ttk.Label(self.box, text='', anchor='center')
        self.info.pack(fill='x', pady=5)
        self.groups = {}
        self.reload()

    def reload(self):
        try:
            groups = load_source(self.source.get())
        except Exception as exc:
            messagebox.showerror('Cannot load animation', str(exc))
            return
        self.groups = groups
        actions = sorted({a for a, _ in groups})
        labels = {0: 'Walk', 4: 'Idle'} if self.source.get().startswith('Human ') else {0: 'Walk', 1: 'Idle', 17: 'Fidget 1', 18: 'Fidget 2'}
        names = [f'{a} — ' + labels.get(a, 'Action') for a in actions]
        old = int(self.action.get().split(' ')[0])
        self.actions['values'] = names
        self.action.set(next((name for a, name in zip(actions, names) if a == old), names[0]))
        self.app.reset()

    def frames(self):
        return self.groups.get((int(self.action.get().split(' ')[0]), self.app.direction.get()), [])

    def draw(self, index):
        c = self.canvas
        c.delete('all')
        w, h = c.winfo_width(), c.winfo_height()
        scale = self.app.zoom.get()
        x, y = w // 2, h // 2 + 45 * scale
        if self.app.guides.get():
            c.create_line(x, 0, x, h, fill='#58615b')
            c.create_line(0, y, w, y, fill='#58615b')
            c.create_text(x + 8, y + 12, text='UO root', fill='#89958b', anchor='w')
        frames = self.frames()
        if not frames:
            c.create_text(w // 2, h // 2, text='No frames for this direction in this source.', fill='#eeeecc', justify='center')
            self.info.config(text='Unavailable — no substitute direction displayed')
            return
        record, sprite = frames[index % len(frames)]
        sprite = sprite.resize((sprite.width * scale, sprite.height * scale), Image.Resampling.NEAREST)
        self.photo = ImageTk.PhotoImage(sprite)
        # Never center using a frame's visible bounds or foot position.
        left = x - record['X'] * scale
        top = y - (record['Y'] + record['Height']) * scale
        c.create_image(left, top, image=self.photo, anchor='nw')
        if self.app.guides.get() and 'PelvisX' in record:
            px, py = left + record['PelvisX'] * scale, top + record['PelvisY'] * scale
            c.create_line(px-6, py, px+6, py, fill='#70e7dd')
            c.create_line(px, py-6, px, py+6, fill='#70e7dd')
        if self.app.bounds.get():
            c.create_rectangle(left, top, left + sprite.width, top + sprite.height, outline='#cf9c60')
        mirror_note = f"    Mirrored from {record['MirroredFrom']}" if 'MirroredFrom' in record else ''
        self.info.config(text=f"Frame {record['Frame']} / {len(frames)-1}    {record['Width']}×{record['Height']}    X {record['X']}  Y {record['Y']}{mirror_note}")


class Viewer:
    def __init__(self, root):
        self.root = root
        root.title('Shard Motion Studio — UO animation viewer')
        root.geometry('1120x720')
        root.minsize(900, 650)
        self.index = 0
        self.playing = True
        self.panels = []
        self.direction = tk.IntVar(value=0)
        self.zoom = tk.IntVar(value=3)
        self.fps = tk.IntVar(value=8)
        self.guides = tk.BooleanVar(value=True)
        self.bounds = tk.BooleanVar(value=False)
        bar = ttk.Frame(root, padding=10)
        bar.pack(fill='x')
        self.play = ttk.Button(bar, text='Pause', command=self.toggle)
        self.play.pack(side='left')
        ttk.Button(bar, text='◀ Frame', command=lambda: self.step(-1)).pack(side='left', padx=4)
        ttk.Button(bar, text='Frame ▶', command=lambda: self.step(1)).pack(side='left')
        for label, var, values in [('Direction', self.direction, list(range(8))), ('Zoom', self.zoom, [1, 2, 3, 4]), ('FPS', self.fps, [2, 4, 6, 8, 10, 12, 16, 20])]:
            ttk.Label(bar, text=label).pack(side='left', padx=(12, 4))
            combo = ttk.Combobox(bar, textvariable=var, values=values, width=4, state='readonly')
            combo.pack(side='left')
            combo.bind('<<ComboboxSelected>>', lambda _: self.reset())
        ttk.Checkbutton(bar, text='Root guides', variable=self.guides, command=self.draw).pack(side='left', padx=8)
        ttk.Checkbutton(bar, text='Bounds', variable=self.bounds, command=self.draw).pack(side='left')
        ttk.Button(bar, text='Reload files', command=self.reload).pack(side='right')
        ttk.Label(root, text='0 front · 1 front-left · 2 left · 3 rear-left · 4 back · 5 rear-right · 6 right · 7 front-right | 5–7 mirrored', padding=8).pack(fill='x')
        body = ttk.Frame(root)
        body.pack(fill='both', expand=True)
        human = '--human' in sys.argv
        trial = '--fluid' in sys.argv
        stride = '--stride' in sys.argv
        right_first = '--right-first' in sys.argv
        fidget = '--fidget' in sys.argv
        self.panels.append(Panel(self, body, 'Animation', 'Idle-first FIDGETS' if fidget else 'Idle-first RIGHT then LEFT' if right_first else 'UO stride matching trial — BLACK FUR' if stride else 'Alternating step trial (front only)' if trial else 'Human male (body 400)' if human else 'New walk draft (front only)', 17 if fidget else 0))
        self.panels.append(Panel(self, body, 'Compare with', 'Idle-first FIDGETS' if fidget else 'Idle-first RIGHT then LEFT' if right_first else 'Original UO' if stride else 'Human male (body 400)' if human else 'Current installed revision', 1 if fidget or right_first else 0 if stride else 4 if human else 1))
        self.scrub = ttk.Scale(root, from_=0, to=9, command=self.seek)
        self.scrub.pack(fill='x', padx=15, pady=8)
        ttk.Label(root, text='Drafts are not installed. “Current installed revision” reads the verified exported revision, not live MUL files.', padding=8).pack(fill='x')
        root.bind('<space>', lambda _: self.toggle())
        root.bind('<Left>', lambda _: self.step(-1))
        root.bind('<Right>', lambda _: self.step(1))
        self.updating = False
        self.tick()

    def count(self):
        return max([len(p.frames()) for p in self.panels] + [1])

    def reset(self):
        self.index = 0
        self.draw()

    def draw(self):
        for panel in self.panels:
            panel.draw(self.index)
        if hasattr(self, 'scrub'):
            self.updating = True
            self.scrub.config(to=max(1, self.count()-1))
            self.scrub.set(self.index)
            self.updating = False

    def seek(self, value):
        if getattr(self, 'updating', False):
            return
        self.playing = False
        self.play.config(text='Play')
        self.index = int(float(value))
        self.draw()

    def toggle(self):
        self.playing = not self.playing
        self.play.config(text='Pause' if self.playing else 'Play')

    def step(self, amount):
        self.playing = False
        self.play.config(text='Play')
        self.index = (self.index + amount) % self.count()
        self.draw()

    def reload(self):
        for panel in self.panels:
            panel.reload()

    def tick(self):
        self.draw()
        if self.playing:
            self.index = (self.index + 1) % self.count()
        self.root.after(round(1000 / self.fps.get()), self.tick)


if __name__ == '__main__':
    if '--self-test' in sys.argv:
        for source in SOURCES:
            groups = load_source(source)
            print(source, sum(map(len, groups.values())), 'frames OK')
        root = tk.Tk()
        app = Viewer(root)
        root.update()
        app.step(1)
        for direction in range(8):
            app.direction.set(direction)
            app.draw()
        combined = load_source('Idle-first RIGHT then LEFT')
        fidgets = load_source('Idle-first FIDGETS')
        for d in range(8):
            for a in (17,18):
                assert len(fidgets[(a,d)]) == 5
                m,p = fidgets[(a,d)][0]
                idle_m,idle_p = fidgets[(1,d)][0]
                assert p.tobytes() == idle_p.tobytes()
                assert (m['X'],m['Y']) == (idle_m['X'],idle_m['Y'])
        assert all(len(combined[(0, d)]) == 10 and len(combined[(1, d)]) == 1 for d in range(8))
        for d in (1,2,3):
            for (_, original), (_, mirrored) in zip(combined[(0,d)], combined[(0,8-d)]):
                assert original.tobytes() == mirrored.transpose(Image.Transpose.FLIP_LEFT_RIGHT).tobytes()
        app.reload()
        root.update()
        root.destroy()
        print('GUI construction, stepping, missing-direction handling and reload OK')
    else:
        root = tk.Tk()
        Viewer(root)
        root.mainloop()
