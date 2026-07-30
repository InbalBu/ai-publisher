<?php
/**
 * Plugin Name: Mekomon AI Bridge
 * Description: Exposes the Yoast SEO meta keys the publisher app needs to write, over the REST API. Nothing else. Install as a must-use plugin (copy to wp-content/mu-plugins/).
 * Version: 1.1.0
 */

if (!defined('ABSPATH')) {
    exit;
}

add_action('init', function () {
    $keys = [
        '_yoast_wpseo_title',
        '_yoast_wpseo_metadesc',
        '_yoast_wpseo_focuskw',
        // Pins the exact Facebook/WhatsApp (Open Graph) share-preview image to
        // the post's featured image instead of relying on Yoast's automatic
        // fallback, which is what went missing on the post that shared with
        // no preview.
        '_yoast_wpseo_opengraph-image',
        '_yoast_wpseo_opengraph-image-id',
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
