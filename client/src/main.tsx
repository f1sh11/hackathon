/// <reference types="vite/client" />
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import './styles/global.css';

const appRoot = createRoot(document.getElementById('root')!);
appRoot.render(<App />);
if (import.meta.hot) {
  import.meta.hot.dispose(() => appRoot.unmount());
}
