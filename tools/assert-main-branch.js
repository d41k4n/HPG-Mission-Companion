#!/usr/bin/env node

'use strict';

// Start gate: the repository has one branch (main) and one registered worktree.
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const root = path.resolve(__dirname, '..');

function git(args) {
  return cp.execFileSync('git', ['-C', root, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

function main() {
  const head = fs.readFileSync(path.join(root, '.git', 'HEAD'), 'utf8').trim();
  const branch = head.startsWith('ref: refs/heads/') ? head.slice('ref: refs/heads/'.length) : '';
  if (branch !== 'main') {
    console.error(`Branch guard FAIL: work only on main (current: ${branch || 'detached HEAD'}).`);
    process.exit(1);
  }
  const hooks = (() => { try { return git(['config', 'core.hooksPath']); } catch (e) { return ''; } })();
  if (hooks !== '.githooks') {
    console.error('Branch guard FAIL: run "git config core.hooksPath .githooks".');
    process.exit(1);
  }
  const trees = git(['worktree', 'list', '--porcelain']).split(/\r?\n/).filter((l) => l.startsWith('worktree '));
  if (trees.length !== 1) {
    console.error(`Branch guard FAIL: exactly one registered worktree is allowed, found ${trees.length}.`);
    process.exit(1);
  }
  console.log('Branch guard PASS (main, one worktree, hooks .githooks).');
}

main();
