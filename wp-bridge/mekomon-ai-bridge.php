<?php
/**
 * Plugin Name: Mekomon AI Bridge
 * Description: Exposes the three Yoast SEO meta keys the publisher app needs to write, over the REST API. Nothing else. Install as a must-use plugin (copy to wp-content/mu-plugins/).
 * Version: 1.0.0
 */

if (!defined('ABSPATH')) {
    exit;
}

add_action('init', function () {
    $keys = [
        '_yoast_wpseo_title',
        '_yoast_wpseo_metadesc',
        '_yoast_wpseo_focuskw',
    ];

    foreach ($keys as $key) {
        register_post_meta('post', $key, [
            'type' => 'string',
            'single' => true,
            'show_in_rest' => true,
            'sanitize_callback' => 'sanitize_text_field',
            'auth_callback' => function () {
                return current_user_can('edit_posts');
            },
        ]);
    }
});
