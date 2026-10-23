import '@fontsource-variable/inter';
import '@fontsource/geist-mono/500.css';
import './styles/tokens.css';
import './styles/base.css';
import '@xyflow/svelte/dist/style.css';
import { mount } from 'svelte';
import App from './App.svelte';

mount(App, { target: document.getElementById('app')! });
