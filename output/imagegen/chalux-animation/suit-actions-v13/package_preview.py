"""Mechanical extraction of newly generated art; no pose repainting."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
SIZE = (896, 592)
SPECS = {
    'attack': {'file': 'registered-sheet.png', 'rows': [0, 367, 724, 1086], 'scale': 1.42,
               'crops': [(0,327),(327,670),(670,1000),(1000,1448),
                         (0,329),(329,666),(666,1125),(1100,1448),
                         (0,380),(375,670),(670,1060),(1090,1448)],
               'feet': [(95,185),(400,490),(750,835),(1180,1270),
                        (90,180),(405,490),(745,835),(1180,1270),
                        (80,170),(425,515),(760,850),(1180,1270)],
               'target': 350, 'times': [90,90,90,120,50,40,50,70,80,100,100,120]},
    'defeat': {'file': 'generated-sheet.png', 'rows': [0,360,680,980,1254], 'scale': 1.50,
               'feet': [(182,266),(183,270),(196,284),(208,295),
                        (200,283),(205,294),(211,310),(214,313),
                        (197,290),(207,300),(215,313),(220,313)],
               'target': 487, 'times': [100,110,110,110,120,120,120,130,130,140,140,300]},
}

def bounds(im):
    a=np.array(im)
    yy,xx=np.where(a[:,:,3]>128)
    return [int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1]

def gif(frames,times,path):
    rgb=[]
    for im in frames:
        bg=Image.new('RGBA',im.size,'#172337');bg.alpha_composite(im);rgb.append(bg.convert('RGB'))
    strip=Image.new('RGB',(rgb[0].width*len(rgb),rgb[0].height))
    for i,im in enumerate(rgb):strip.paste(im,(i*im.width,0))
    palette=strip.quantize(colors=256,dither=Image.Dither.NONE)
    q=[im.quantize(palette=palette,dither=Image.Dither.NONE) for im in rgb]
    q[0].save(path,save_all=True,append_images=q[1:],duration=times,loop=0,optimize=False,disposal=2)

all_frames={}
manifest={'mode':'built-in image_gen','version':'v13','status':'preview-only',
          'frameSize':SIZE,'gameAssetsReplaced':False,'animations':{}}
for name,spec in SPECS.items():
    sheet=Image.open(HERE/name/spec['file']).convert('RGBA')
    frames,records=[],[]
    for i in range(12):
        row,col=divmod(i,4);top,bottom=spec['rows'][row:row+2]
        left,right=spec['crops'][i] if name=='attack' else (round(col*sheet.width/4),round((col+1)*sheet.width/4))
        raw=sheet.crop((left,top,right,bottom))
        a=np.array(raw).astype(int);yy,xx=np.indices(a.shape[:2]);gx,gy=xx+left,yy+top
        # Preserve full swords crossing nominal cells; exclude only adjacent poses.
        if name=='attack' and i==6:a[(gx>1090)&(gy>580)]=0
        if name=='attack' and i==7:a[(gx<1125)&(gy<560)]=0
        raw=Image.fromarray(a.astype('uint8'))
        lo,hi=spec['feet'][i]
        if name=='defeat':lo+=left;hi+=left
        solid=a[:,:,3]>128
        last=int(np.where(solid)[0].max())
        foot=((a[:,:,3]>180)&(a[:,:,2]>a[:,:,0]+45)&(a[:,:,2]>a[:,:,1]+20)
              &(yy>last-25)&(gx>=lo)&(gx<=hi))
        fy,fx=np.where(foot)
        center=(int(fx.min())+int(fx.max()))/2
        contact=solid&(yy>last-28)&(xx>=fx.min()-4)&(xx<=fx.max()+4)
        foot_bottom=int(np.where(contact)[0].max())
        scale=spec['scale']
        scaled=raw.resize((round(raw.width*scale),round(raw.height*scale)),Image.Resampling.NEAREST)
        dx=round(spec['target']-center*scale);dy=round(535-(foot_bottom+1)*scale)+1
        frame=Image.new('RGBA',SIZE);frame.paste(scaled,(dx,dy))
        frames.append(frame)
        records.append({'sourceFrame':i+1,'sourceRect':[left,top,right-left,bottom-top],
                        'scale':scale,'registration':[dx,dy],'sourceFootCenter':center,
                        'sourceFootBottom':foot_bottom})
    # Keep the new generated neutral pose at both ends; no old animation poses.
    if name=='attack':frames[-1]=frames[0].copy();records[-1]['reusesOutputFrame']=1
    out=HERE/name/'frames';out.mkdir(exist_ok=True)
    atlas=Image.new('RGBA',(SIZE[0]*4,SIZE[1]*3));contact=Image.new('RGBA',atlas.size,'#172337')
    for i,im in enumerate(frames):
        box=bounds(im)
        assert box[0]>0 and box[1]>0 and box[2]<SIZE[0] and box[3]<SIZE[1],(name,i+1,box)
        records[i]['outputBounds']=box
        im.save(out/f'{name}-{i+1:02}.png')
        pos=(i%4*SIZE[0],i//4*SIZE[1]);atlas.paste(im,pos);contact.alpha_composite(im,pos)
    atlas.save(HERE/name/f'{name}-sheet.png')
    contact.resize((1792,888),Image.Resampling.NEAREST).save(HERE/name/'qa-contact.png')
    gif(frames,spec['times'][:-1]+[1000],HERE/name/f'{name}-preview.gif')
    all_frames[name]=frames
    manifest['animations'][name]={'frameCount':12,'durationMs':spec['times'],'totalMs':sum(spec['times']),
                                  'loop':False,'extraction':records}
assert all_frames['attack'][0].tobytes()==all_frames['attack'][-1].tobytes()
(HERE/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
panels=[]
for tick in range(62):
    canvas=Image.new('RGBA',(1344,490),'#172337');draw=ImageDraw.Draw(canvas)
    for j,name in enumerate(SPECS):
        idx,total=11,0
        for i,ms in enumerate(SPECS[name]['times']):
            total+=ms
            if tick*50<total:idx=i;break
        draw.text((j*672+24,8),'新攻击：横砍' if name=='attack' else '新战败',font=font,fill='#edf2ff')
        canvas.alpha_composite(all_frames[name][idx].resize((672,444),Image.Resampling.NEAREST),(j*672,42))
    panels.append(canvas)
gif(panels,[50]*len(panels),HERE/'two-actions-preview.gif')
panels[12].save(HERE/'preview-still.png')
for n,s in manifest['animations'].items():
    print(n,'frames',s['frameCount'],'time',s['totalMs'],'head tops',[r['outputBounds'][1] for r in s['extraction']])
