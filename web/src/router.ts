import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

/**
 * Views are lazily loaded so the landing page — the only one most visitors see — does not carry
 * the weight of pdf.js, which the form designer needs but nothing else does.
 */
const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: () => import('./views/HomeView.vue') },
  { path: '/create', name: 'create', component: () => import('./views/CreateTextView.vue') },
  { path: '/word', name: 'word', component: () => import('./views/WordView.vue') },
  { path: '/edit', name: 'edit', component: () => import('./views/EditTextView.vue') },
  { path: '/merge', name: 'merge', component: () => import('./views/MergeView.vue') },
  { path: '/pages', name: 'pages', component: () => import('./views/PagesView.vue') },
  { path: '/forms', name: 'forms', component: () => import('./views/FormsView.vue') },
  { path: '/summarize', name: 'summarize', component: () => import('./views/SummarizeView.vue') },
  { path: '/inspect', name: 'inspect', component: () => import('./views/InspectView.vue') },
  { path: '/api', name: 'api', component: () => import('./views/ApiView.vue') },
  { path: '/:catchAll(.*)', redirect: '/' },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})
