// Optional PNG previews and visual contact sheet; requires Playwright + Chromium.
import {createRequire} from 'node:module';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
const require=createRequire(import.meta.url),{chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const directory=path.resolve(process.argv[2]||'Artifacts/Vector-Art-Pack');
const manifest=JSON.parse(await readFile(path.join(directory,'Assets/manifest.json'),'utf8'));
const browser=await chromium.launch({headless:true,...(process.env.CHROME_PATH?{executablePath:process.env.CHROME_PATH}:{})});
const page=await browser.newPage({deviceScaleFactor:2});
const files=Object.values(manifest).filter(x=>typeof x==='object').flatMap(x=>Object.values(x));
for(const file of files){
 const source=await readFile(path.join(directory,'Assets',file),'utf8');
 const width=Number(source.match(/width="(\d+)"/)?.[1]||128),height=Number(source.match(/height="(\d+)"/)?.[1]||128);
 await page.setViewportSize({width,height});await page.setContent(`<style>html,body{margin:0;background:transparent}svg{display:block}</style>${source}`);
 await page.evaluate(()=>document.fonts.ready);
 const output=path.join(directory,'PNG',file.replace(/\.svg$/,'.png'));await mkdir(path.dirname(output),{recursive:true});await page.screenshot({path:output,omitBackground:true});
}
const sheet=await readFile(path.join(directory,'Contact-Sheet.svg'),'utf8');
const height=Number(sheet.match(/height="(\d+)"/)?.[1]||1200);await page.setViewportSize({width:1200,height});await page.setContent(`<style>body{margin:0}</style>${sheet}`);await page.screenshot({path:path.join(directory,'Contact-Sheet.png')});
await browser.close();console.log(`Rendered ${files.length} transparent PNG previews and contact sheet.`);
