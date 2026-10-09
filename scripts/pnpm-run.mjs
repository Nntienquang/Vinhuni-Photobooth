import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
const local=resolve('.tools/node_modules/pnpm/bin/pnpm.cjs');
const cli=process.env.npm_execpath?.includes('pnpm')?process.env.npm_execpath:existsSync(local)?local:null;
if(!cli)throw new Error('Install pnpm 10, then run this command with pnpm.');
const child=spawn(process.execPath,[cli,...process.argv.slice(2)],{stdio:'inherit'});child.on('exit',code=>process.exit(code??1));
