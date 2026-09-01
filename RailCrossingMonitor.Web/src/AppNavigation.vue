<template>
  <v-navigation-drawer permanent>
    <v-list nav>
      <v-list-item
          to="/trains"
          prepend-icon="mdi-train"
          title="Pociągi na żywo"
      />

      <v-list-item
          to="/favorites"
          prepend-icon="mdi-star"
          title="Ulubione"
          disabled
      />

      <v-list-item
          to="/statistics"
          prepend-icon="mdi-chart-line"
          title="Statystyki"
          disabled
      />

      <v-list-item
          to="/settings"
          prepend-icon="mdi-cog"
          title="Ustawienia"
          disabled
      />
    </v-list>
  </v-navigation-drawer>
</template>