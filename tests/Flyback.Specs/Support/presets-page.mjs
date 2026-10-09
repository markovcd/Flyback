// Runs the preset site's presets.js on a stand-in document, under Node, and prints what it built as JSON:
//
//   node presets-page.mjs <presets.js> <page> <search> <answers.json>
//
// <page> is the body's data-page (shelf, preset). <answers.json> maps each address the page fetches, with or
// without its query, to the JSON it is answered with; any other address answers 404. Prints every element the
// page asked for by id, as { tag, attributes, text, hidden, disabled, children }.

import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const [script, page, search, answersFile] = process.argv.slice(2);
const answers = JSON.parse(readFileSync(answersFile, 'utf8'));

class Element {
  constructor(tag) {
    this.tagName = tag;
    this.attributes = {};
    this.children = [];
    this.own = '';
    this.hidden = false;
    this.disabled = false;
    this.value = '';
    this.classList = {
      toggle: (name, on) => {
        const names = new Set((this.attributes.class ?? '').split(' ').filter(Boolean));
        if (on ?? !names.has(name)) names.add(name);
        else names.delete(name);
        this.attributes.class = [...names].join(' ');
      },
    };
  }

  get textContent() { return this.own + this.children.map(c => c.textContent).join(''); }

  set textContent(text) {
    this.own = String(text);
    this.children = [];
  }

  get firstChild() { return this.children[0] ?? null; }

  appendChild(child) {
    this.children.push(child);
    return child;
  }

  replaceChildren(...children) {
    this.own = '';
    this.children = children;
  }

  setAttribute(name, value) { this.attributes[name] = String(value); }

  getAttribute(name) { return this.attributes[name] ?? null; }

  addEventListener() {}

  /** Only `.name`, which is all the page asks. */
  querySelectorAll(selector) {
    const name = selector.slice(1);
    const found = [];
    const walk = e => e.children.forEach(c => {
      if ((c.attributes.class ?? '').split(' ').includes(name)) found.push(c);
      walk(c);
    });
    walk(this);
    return found;
  }

  toJSON() {
    return {
      tag: this.tagName,
      attributes: this.attributes,
      text: this.own,
      hidden: this.hidden,
      disabled: this.disabled,
      children: this.children,
    };
  }
}

const byId = {};
const body = new Element('body');
body.setAttribute('data-page', page);

const document = {
  body,
  title: '',
  createElement: tag => new Element(tag),
  getElementById: id => byId[id] ??= new Element('div'),
};

function fetch(address) {
  const answer = answers[address] ?? answers[String(address).split('?')[0]];

  return Promise.resolve(answer === undefined
    ? { ok: false, status: 404, json: () => Promise.reject(new Error('404')) }
    : { ok: true, status: 200, json: () => Promise.resolve(answer) });
}

const context = vm.createContext({
  document,
  fetch,
  location: { search, href: '' },
  history: { replaceState() {} },
  URLSearchParams,
  Promise,
  setTimeout: () => 0,
  clearTimeout() {},
  scrollTo() {},
  alert() {},
  FlybackRatings: { stars: () => new Element('span'), widget: () => new Element('div') },
  FlybackReports: { form: () => new Element('form') },
});

vm.runInContext(readFileSync(script, 'utf8'), context, { filename: script });

// Every fetch answers at once, so a few turns of the loop settle every chain the page started.
for (let turn = 0; turn < 20; turn++) await new Promise(resolve => setImmediate(resolve));

process.stdout.write(JSON.stringify(byId));
