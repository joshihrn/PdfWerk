import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router'

// Order matters: tokens define the variables that base and every component consume.
import './styles/tokens.css'
import './styles/base.css'

createApp(App).use(router).mount('#app')
